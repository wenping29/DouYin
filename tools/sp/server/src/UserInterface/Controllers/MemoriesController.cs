using Microsoft.AspNetCore.Mvc;
using SpServer.Application;
using SpServer.Contracts;
using SpServer.Domain;

namespace SpServer.UserInterface.Controllers;

// 用户长期记忆：仅登录用户可读写；匿名设备不记录，换设备登录同一账号可恢复
[ApiController]
[Route("api/memories")]
public sealed class MemoriesController(MemoryStore store, UserAuth auth) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await store.ListAsync(await RequireUserIdAsync(ct), ct));

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] MemoryCreateRequest req, CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(ct);
        return Ok(await store.AddAsync(userId, req.Content, ct));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> Settings([FromBody] MemorySettingsRequest req, CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(ct);
        var enabled = await store.SetUserEnabledAsync(userId, req.Enabled ?? true, ct);
        return Ok(new { enabled });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] MemoryUpdateRequest req, CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(ct);
        return Ok(await store.UpdateAsync(userId, id, req.Content, req.Enabled, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(ct);
        return Ok(new { deleted = await store.DeleteAsync(userId, id, ct) });
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var userId = await RequireUserIdAsync(ct);
        return Ok(new { deleted = await store.ClearAsync(userId, ct) });
    }

    private async Task<int> RequireUserIdAsync(CancellationToken ct)
    {
        var account = await auth.ResolveAccountAsync(ApiHelpers.Bearer(Request), ct)
            ?? throw new ApiException(401, "bad_token", "登录已失效，请重新登录");
        return account.UserId ?? throw new ApiException(403, "login_required", "登录后才能使用记忆功能");
    }
}
