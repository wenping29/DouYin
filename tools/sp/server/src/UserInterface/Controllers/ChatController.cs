using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SpServer.Contracts;
using SpServer.Domain;
using SpServer.Infrastructure;
using SpServer.Application;

namespace SpServer.UserInterface.Controllers;

// 对话 SSE：预扣积分后流式转发，失败/取消整笔退回；登录用户会注入长期记忆并在结束后异步抽取
[ApiController]
[Route("api")]
public sealed class ChatController(AppDbContext db, CreditService credits, ModelGateway gateway,
    ProviderKey keys, MemoryStore memories, MemoryExtractionQueue extractionQueue,
    IConfiguration config, ILogger<ChatController> logger) : ControllerBase
{
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        var account = await credits.AuthenticateAsync(ApiHelpers.Bearer(Request), ct);
        var policy = await credits.GetPolicyAsync(ct);
        if (!policy.ChatEnabled) throw new ApiException(503, "chat_disabled", "服务当前已暂停，请稍后再试");

        var model = await db.Models.FirstOrDefaultAsync(m =>
                m.ModelId == req.Model && m.IsEnabled && !m.IsDeprecated, ct)
            ?? throw new ApiException(404, "model_not_found", "模型不可用，请重新选择");

        if (!await keys.IsUsableAsync(model.Provider, ct))
        {
            throw new ApiException(503, "provider_unavailable", "该模型所属服务暂未开放，请选择其他模型");
        }

        var messages = ApiHelpers.BuildMessages(req, model);
        var lastUserText = req.Messages?.LastOrDefault(m =>
            m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content))?.Content ?? "";

        // 登录用户且记忆开关打开时，把长期记忆追加到 system prompt
        if (account.UserId is { } memoryUserId)
        {
            var memoryBlock = await memories.BuildPromptAsync(memoryUserId, policy.MemoryEnabled, ct);
            if (!string.IsNullOrEmpty(memoryBlock))
            {
                messages[0] = messages[0] with { Content = messages[0].Content + "\n\n" + memoryBlock };
            }
        }

        var maxTokens = Math.Clamp(req.MaxTokens ?? config.GetValue("MaxOutputTokens", 1024), 128, 4096);
        var estimatedInput = messages.Sum(TokenEstimator.EstimateMessage);
        var reserved = TokenEstimator.Charge(estimatedInput, model.InputRatePer1K)
                     + TokenEstimator.Charge(maxTokens, model.OutputRatePer1K);

        await credits.ReserveAsync(account.Id, reserved, ct);

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        string? error = null;
        var usage = new CompletionUsage(estimatedInput, 0, false);
        var answer = new StringBuilder();

        try
        {
            usage = await gateway.StreamAsync(model, messages, maxTokens,
                (kind, piece) =>
                {
                    if (kind == DeltaKind.Content) answer.Append(piece);
                    return ApiHelpers.WriteEvent(Response,
                        new { type = kind == DeltaKind.Reasoning ? "reasoning" : "delta", text = piece }, ct);
                }, ct);
        }
        catch (Exception ex)
        {
            error = ex switch
            {
                ApiException apiEx => apiEx.Message,
                OperationCanceledException => "请求已取消",
                _ => "连接模型服务时出错，请重试",
            };
            logger.LogWarning(ex, "模型调用失败：{Model}", req.Model);
        }

        // 失败与取消都按 0 用量结算，把预扣的积分整笔退回
        var actual = error is null ? usage : new CompletionUsage(0, 0, false);
        var balance = await credits.SettleAsync(account, model, reserved, actual.InputTokens, actual.OutputTokens,
            error is null ? "chat" : "refund", CancellationToken.None);

        try
        {
            await ApiHelpers.WriteEvent(Response, error is null
                ? new
                {
                    type = "done",
                    usage = new
                    {
                        inputTokens = usage.InputTokens, outputTokens = usage.OutputTokens,
                        fromUpstream = usage.FromUpstream,
                    },
                    balance,
                }
                : new { type = "error", error, balance }, CancellationToken.None);
        }
        catch (IOException)
        {
            // 客户端已断开，事件写不出去不影响结算结果
        }

        // 只在成功回答后异步抽取记忆；平台承担费用，失败不影响本次对话
        if (error is null && account.UserId is { } extractUserId && answer.Length > 0 &&
            lastUserText.Trim().Length > 0)
        {
            extractionQueue.TryEnqueue(new MemoryExtractionJob(extractUserId, lastUserText, answer.ToString()));
        }

        return new EmptyResult();
    }
}
