using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SpServer.Domain;
using SpServer.Infrastructure;

namespace SpServer.Application;

public record MemoryExtractionJob(int UserId, string UserText, string Answer);

// 自动抽取是平台承担费用的后台任务：先入有界队列，再由后台服务异步处理，不阻塞聊天响应
public sealed class MemoryExtractionQueue(IConfiguration config)
{
    private static readonly ConcurrentDictionary<int, DateTime> LastRun = new();
    private readonly Channel<MemoryExtractionJob> _channel =
        Channel.CreateBounded<MemoryExtractionJob>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public bool TryEnqueue(MemoryExtractionJob job)
    {
        if (job.UserId <= 0 || job.UserText.Trim().Length == 0 || job.Answer.Trim().Length == 0) return false;

        var minutes = Math.Max(0, config.GetValue("Memory:ExtractIntervalMinutes", 10));
        if (minutes > 0)
        {
            var now = DateTime.UtcNow;
            if (LastRun.TryGetValue(job.UserId, out var last) && now - last < TimeSpan.FromMinutes(minutes))
            {
                return false;
            }
            LastRun[job.UserId] = now;
        }

        return _channel.Writer.TryWrite(job);
    }

    internal IAsyncEnumerable<MemoryExtractionJob> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

public sealed class MemoryExtractionWorker(
    MemoryExtractionQueue queue,
    IServiceScopeFactory scopes,
    ILogger<MemoryExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<MemoryExtractionService>();
                    await service.ExtractAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 记忆抽取失败不能影响聊天主流程，只记日志
                    logger.LogWarning(ex, "自动抽取记忆失败：用户 {UserId}", job.UserId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

// 用便宜的模型从一轮问答里抽取长期偏好/事实；费用平台承担，不写积分流水
public sealed class MemoryExtractionService(
    AppDbContext db,
    ModelGateway gateway,
    ProviderKey keys,
    IConfiguration config,
    ILogger<MemoryExtractionService> logger)
{
    private const int MaxNewItems = 3;
    private const int MaxInputChars = 3000;

    private const string ExtractSystem =
        "你是长期记忆抽取器。只提取用户在对话中明确表达、未来仍然有用的稳定信息，" +
        "例如称呼、职业、语言偏好、长期目标、常用技术栈、明确的写作/回答偏好。" +
        "不要提取一次性的问题、临时任务、网页内容、模型自己的猜测。" +
        "禁止提取密码、密钥、身份证号、银行卡号、手机号、住址等敏感信息。" +
        "只输出 JSON 字符串数组，不要输出任何解释或 Markdown；没有可记的内容就输出 []。" +
        "每条不超过 60 个汉字，最多 3 条，必须是可以独立理解的完整短句。";

    public async Task ExtractAsync(MemoryExtractionJob job, CancellationToken ct)
    {
        if (!config.GetValue("Memory:AutoExtract", true)) return;

        var policyEnabled = await db.Policies.AsNoTracking()
            .Select(p => (bool?)p.MemoryEnabled).FirstOrDefaultAsync(ct) ?? true;
        if (!policyEnabled) return;

        var userEnabled = await db.Users.AsNoTracking()
            .Where(u => u.Id == job.UserId)
            .Select(u => (bool?)u.MemoryEnabled).FirstOrDefaultAsync(ct);
        if (userEnabled != true) return;

        var model = await PickModelAsync(ct);
        if (model is null) return;

        var existing = await db.UserMemories.AsNoTracking()
            .Where(m => m.UserId == job.UserId)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(MemoryStore.MaxItems)
            .Select(m => m.Content)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing.Select(NormalizeForCompare));

        var userPayload = new StringBuilder()
            .AppendLine("已有记忆（不要重复）：")
            .AppendLine(existing.Count == 0 ? "（空）" : string.Join("\n", existing.Select(x => "- " + x)))
            .AppendLine()
            .AppendLine("本轮用户消息：")
            .AppendLine(Clamp(job.UserText, MaxInputChars))
            .AppendLine()
            .AppendLine("本轮助手回复：")
            .AppendLine(Clamp(job.Answer, MaxInputChars))
            .ToString();

        var messages = new List<ChatMessage>
        {
            new("system", ExtractSystem),
            new("user", userPayload),
        };

        var answer = new StringBuilder();
        try
        {
            await gateway.StreamAsync(model, messages, 300,
                (kind, piece) =>
                {
                    if (kind == DeltaKind.Content) answer.Append(piece);
                    return Task.CompletedTask;
                }, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "记忆抽取模型调用失败：用户 {UserId}", job.UserId);
            return;
        }

        var candidates = ParseCandidates(answer.ToString());
        if (candidates.Count == 0) return;

        var total = await db.UserMemories.CountAsync(m => m.UserId == job.UserId, ct);
        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var candidate in candidates)
        {
            if (added >= MaxNewItems || total >= MemoryStore.MaxItems) break;
            var content = string.Join(' ', candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (content.Length < 4 || LooksSensitive(content)) continue;
            if (content.Length > MemoryStore.MaxContentChars) content = content[..MemoryStore.MaxContentChars];
            if (!existingSet.Add(NormalizeForCompare(content))) continue;

            db.UserMemories.Add(new UserMemory
            {
                UserId = job.UserId,
                Content = content,
                Source = "auto",
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
            total++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("自动写入 {Count} 条长期记忆：用户 {UserId}", added, job.UserId);
        }
    }

    private async Task<ModelSpec?> PickModelAsync(CancellationToken ct)
    {
        var preferred = (config["Memory:ExtractModel"] ?? "glm-4-flash").Trim();
        if (preferred.Length > 0)
        {
            var match = await db.Models.AsNoTracking().FirstOrDefaultAsync(m =>
                m.ModelId == preferred && m.IsEnabled && !m.IsDeprecated, ct);
            if (match is not null && await keys.IsUsableAsync(match.Provider, ct)) return match;
        }

        // 首选模型不可用时退到费率最低的可用模型；仍然失败就跳过本轮抽取
        var fallback = await db.Models.AsNoTracking()
            .Where(m => m.IsEnabled && !m.IsDeprecated)
            .OrderBy(m => m.InputRatePer1K + m.OutputRatePer1K)
            .ToListAsync(ct);
        foreach (var model in fallback)
        {
            if (await keys.IsUsableAsync(model.Provider, ct)) return model;
        }
        return null;
    }

    private static List<string> ParseCandidates(string raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return [];

        // 兼容模型偶尔包一层 ```json 代码块
        if (text.StartsWith("```"))
        {
            var firstLine = text.IndexOf('\n');
            if (firstLine >= 0) text = text[(firstLine + 1)..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) text = text[..fence];
            text = text.Trim();
        }

        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return [];
        text = text[start..(end + 1)];

        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var value = item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Object when item.TryGetProperty("content", out var content) => content.GetString(),
                    JsonValueKind.Object when item.TryGetProperty("text", out var textValue) => textValue.GetString(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return result;
    }

    private static string NormalizeForCompare(string value) =>
        new string(value.Trim().Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();

    // 自动抽取可能误存敏感信息，落库前再拦一层；宁可少记，不把密钥/证件写进长期记忆
    private static bool LooksSensitive(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("密码") || value.Contains("密钥") || value.Contains("授权码") ||
               value.Contains("api key") || value.Contains("apikey") || value.Contains("token") ||
               value.Contains("身份证") || value.Contains("银行卡") || value.Contains("cvv");
    }

    private static string Clamp(string text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max];
}
