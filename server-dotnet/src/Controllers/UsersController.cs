using Microsoft.AspNetCore.Mvc;

namespace DouYing.Server.Controllers;

public sealed class UsersController(DataStore db, AuthService auth, BackendPaths paths)
    : ApiControllerBase(db, auth, paths)
{
    [HttpGet("me")]
    public async Task<IResult> GetMe()
    {
        var context = HttpContext;

        var user = await RequireUser(context, db, auth);
        return Json(new { user = PublicUser(user) });
    }

    [HttpPatch("me")]
    public async Task<IResult> UpdateMe()
    {
        var context = HttpContext;

        var user = await RequireUser(context, db, auth);
        var body = await ReadJson(context);
        var hasNickname = HasProperty(body, "nickname");
        var hasBio = HasProperty(body, "bio");
        if (!hasNickname && !hasBio)
            throw new ApiException(400, "VALIDATION_ERROR", "没有需要更新的字段");
        var nickname = hasNickname ? Text(Property(body, "nickname"), 20) : String(user, "nickname");
        var bio = hasBio ? Text(Property(body, "bio"), 80) : String(user, "bio");
        if (nickname.Length == 0)
            throw new ApiException(400, "VALIDATION_ERROR", "昵称不能为空");
        await db.ExecuteAsync("UPDATE users SET nickname = ?, bio = ? WHERE id = ?", nickname, bio, Number(user, "id"));
        return Json(new { user = PublicUser(await GetUser(db, Number(user, "id"))) });
    }

    [HttpPost("me/avatar")]
    public async Task<IResult> UploadAvatar()
    {
        var context = HttpContext;

        var user = await RequireUser(context, db, auth);
        var form = await ReadForm(context);
        var file = form.Files.GetFile("avatar");
        if (file is null)
            throw new ApiException(400, "VALIDATION_ERROR", "请上传头像图片");
        ValidateFile(file, false, MaxImageBytes);
        var name = NewFileName(file.FileName);
        await SaveFile(file, Path.Combine(mediaRoot, "avatars", name));
        await db.ExecuteAsync("UPDATE users SET avatar_url = ? WHERE id = ?", $"/media/avatars/{name}", Number(user, "id"));
        RemoveMediaFile(mediaRoot, String(user, "avatar_url"));
        return Json(new { user = PublicUser(await GetUser(db, Number(user, "id"))) });
    }

    [HttpGet("users/suggested")]
    public async Task<IResult> GetSuggestedUsers()
    {
        var context = HttpContext;

        var viewer = await OptionalUser(context, db, auth);
        var limit = Math.Clamp(QueryInt(context, "limit", 8), 1, 20);
        var viewerId = viewer is null ? 0L : Number(viewer, "id");
        var rows = await db.QueryAsync($"""
                {UserSelect}
                WHERE u.id != ?
                  AND u.id NOT IN (SELECT following_id FROM follows WHERE follower_id = ?)
                ORDER BY follower_count DESC, u.id DESC LIMIT ?
                """, viewerId, viewerId, limit);
        return Json(new { items = rows.Select(row => PublicUser(row)) });
    }

    [HttpGet("users/{id:long}")]
    public async Task<IResult> GetUser(long id)
    {
        var context = HttpContext;

        var row = await GetUser(db, id);
        if (row is null)
            throw new ApiException(404, "NOT_FOUND", "用户不存在");
        var viewer = await OptionalUser(context, db, auth);
        var following = viewer is not null && Number(viewer, "id") != id &&
            await db.FirstAsync("SELECT 1 AS ok FROM follows WHERE follower_id = ? AND following_id = ?",
                Number(viewer, "id"), id) is not null;
        return Json(new { user = PublicUser(row, following) });
    }

    [HttpGet("users/{id:long}/videos")]
    public async Task<IResult> GetUserVideos(long id)
    {
        var context = HttpContext;

        if (await GetUser(db, id) is null)
            throw new ApiException(404, "NOT_FOUND", "用户不存在");
        var viewer = await OptionalUser(context, db, auth);
        var type = context.Request.Query["type"] == "liked" ? "liked" : "posts";
        var (limit, offset) = Paging(context, 12, 30);
        var sql = type == "liked"
            ? $"{VideoSelect} JOIN likes l ON l.video_id = v.id WHERE l.user_id = ? ORDER BY l.created_at DESC, v.id DESC LIMIT ? OFFSET ?"
            : $"{VideoSelect} WHERE v.user_id = ? AND v.status = 'published' ORDER BY v.created_at DESC, v.id DESC LIMIT ? OFFSET ?";
        var rows = await db.QueryAsync(sql, id, limit + 1, offset);
        return Json(await VideoPage(db, rows, limit, offset, viewer));
    }

    [HttpPost("users/{id:long}/follow")]
    public async Task<IResult> FollowUser(long id)
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        var viewerId = Number(viewer, "id");
        if (id == viewerId)
            throw new ApiException(400, "VALIDATION_ERROR", "不能关注自己");
        if (await GetUser(db, id) is null)
            throw new ApiException(404, "NOT_FOUND", "用户不存在");
        await db.ExecuteAsync("INSERT INTO follows (follower_id, following_id, created_at) VALUES (?, ?, ?) ON CONFLICT DO NOTHING",
            viewerId, id, db.Timestamp(DateTimeOffset.UtcNow));
        var count = await db.FirstAsync("SELECT COUNT(*) AS c FROM follows WHERE following_id = ?", id);
        return Json(new { following = true, followerCount = Number(count!, "c") });
    }

    [HttpDelete("users/{id:long}/follow")]
    public async Task<IResult> UnfollowUser(long id)
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        await db.ExecuteAsync("DELETE FROM follows WHERE follower_id = ? AND following_id = ?", Number(viewer, "id"), id);
        var count = await db.FirstAsync("SELECT COUNT(*) AS c FROM follows WHERE following_id = ?", id);
        return Json(new { following = false, followerCount = Number(count!, "c") });
    }
}
