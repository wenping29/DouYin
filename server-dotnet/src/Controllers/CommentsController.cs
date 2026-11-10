using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace DouYing.Server.Controllers;

public sealed class CommentsController(DataStore db, AuthService auth, BackendPaths paths)
    : ApiControllerBase(db, auth, paths)
{
    [HttpGet("videos/{id:long}/comments")]
    public async Task<IResult> GetVideoComments(long id)
    {
        var context = HttpContext;

        var video = await GetVideo(db, id);
        if (video is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        var viewer = await OptionalUser(context, db, auth);
        var (limit, offset) = Paging(context, 20, 50);
        var rows = await db.QueryAsync(
            $"{CommentSelect} WHERE c.video_id = ? ORDER BY c.created_at DESC, c.id DESC LIMIT ? OFFSET ?",
            id, limit + 1, offset);
        var hasMore = rows.Count > limit;
        if (hasMore)
            rows = rows.Take(limit).ToList();
        var total = await db.FirstAsync("SELECT COUNT(*) AS c FROM comments WHERE video_id = ?", id);
        return Json(new
        {
            items = rows.Select(row => PublicComment(row, viewer is null ? null : Number(viewer, "id"), Number(video, "user_id"))),
            nextCursor = hasMore ? (string?)(offset + limit).ToString(CultureInfo.InvariantCulture) : null,
            total = Number(total!, "c")
        });
    }

    [HttpPost("videos/{id:long}/comments")]
    public async Task<IResult> AddVideoComment(long id)
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        var video = await GetVideo(db, id);
        if (video is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        var body = await ReadJson(context);
        var content = Text(Property(body, "content"), 500);
        if (content.Length == 0)
            throw new ApiException(400, "VALIDATION_ERROR", "评论不能为空");
        var commentId = await db.TransactionAsync(async (connection, transaction) =>
        {
            var inserted = await DataStore.InsertIdAsync(connection, transaction,
                "INSERT INTO comments (video_id, user_id, content, created_at) VALUES (?, ?, ?, ?) RETURNING id",
                id, Number(viewer, "id"), content, db.Timestamp(DateTimeOffset.UtcNow));
            await DataStore.ExecuteAsync(connection, transaction, "UPDATE videos SET comment_count = comment_count + 1 WHERE id = ?", id);
            return inserted;
        });
        var row = await db.FirstAsync($"{CommentSelect} WHERE c.id = ?", commentId);
        var count = await db.FirstAsync("SELECT comment_count FROM videos WHERE id = ?", id);
        return Json(new
        {
            comment = PublicComment(row!, Number(viewer, "id"), Number(video, "user_id")),
            commentCount = Number(count!, "comment_count")
        }, 201);
    }

    [HttpDelete("comments/{id:long}")]
    public async Task<IResult> DeleteComment(long id)
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        var comment = await db.FirstAsync("""
                SELECT c.*, v.user_id AS video_owner_id
                FROM comments c JOIN videos v ON v.id = c.video_id WHERE c.id = ?
                """, id);
        if (comment is null)
            throw new ApiException(404, "NOT_FOUND", "评论不存在");
        var viewerId = Number(viewer, "id");
        if (Number(comment, "user_id") != viewerId && Number(comment, "video_owner_id") != viewerId)
            throw new ApiException(403, "FORBIDDEN", "没有权限删除这条评论");
        await db.TransactionAsync(async (connection, transaction) =>
        {
            await DataStore.ExecuteAsync(connection, transaction, "DELETE FROM comments WHERE id = ?", id);
            await DataStore.ExecuteAsync(connection, transaction,
                "UPDATE videos SET comment_count = CASE WHEN comment_count > 0 THEN comment_count - 1 ELSE 0 END WHERE id = ?",
                Number(comment, "video_id"));
            return true;
        });
        var updated = await db.FirstAsync("SELECT comment_count FROM videos WHERE id = ?", Number(comment, "video_id"));
        return Json(new { ok = true, commentCount = Number(updated!, "comment_count") });
    }
}
