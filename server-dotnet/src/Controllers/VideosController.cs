using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace DouYing.Server.Controllers;

public sealed class VideosController(DataStore db, AuthService auth, BackendPaths paths)
    : ApiControllerBase(db, auth, paths)
{
    [HttpGet("videos/{id:long}")]
    public async Task<IResult> GetVideo(long id)
    {
        var context = HttpContext;

        var row = await GetVideo(db, id);
        if (row is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        return Json(new { video = (await SerializeVideos(db, [row], await OptionalUser(context, db, auth))).Single() });
    }

    [HttpPost("videos")]
    public async Task<IResult> UploadVideo()
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        var form = await ReadForm(context);
        var video = form.Files.GetFile("video");
        var cover = form.Files.GetFile("cover");
        if (video is null)
            throw new ApiException(400, "VALIDATION_ERROR", "请上传视频文件");
        ValidateFile(video, true, MaxVideoBytes);
        if (cover is not null)
            ValidateFile(cover, false, MaxImageBytes);

        var videoName = NewFileName(video.FileName);
        await SaveFile(video, Path.Combine(mediaRoot, "videos", videoName));
        string? coverName = null;
        try
        {
            if (cover is not null)
            {
                coverName = NewFileName(cover.FileName);
                await SaveFile(cover, Path.Combine(mediaRoot, "covers", coverName));
            }
            var title = Text(form["title"].FirstOrDefault(), 100);
            if (title.Length == 0)
                title = "未命名作品";
            var description = Text(form["description"].FirstOrDefault(), 500);
            var musicName = Text(form["musicName"].FirstOrDefault(), 100);
            _ = double.TryParse(form["duration"].FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
            var videoId = await db.InsertIdAsync(
                """
                    INSERT INTO videos (user_id, title, description, video_url, cover_url, music_name, duration, created_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?) RETURNING id
                    """,
                Number(viewer, "id"), title, description, $"/media/videos/{videoName}",
                coverName is null ? null : $"/media/covers/{coverName}", musicName,
                Math.Max(0, duration), db.Timestamp(DateTimeOffset.UtcNow));
            if (coverName is null)
            {
                coverName = $"v{videoId}.png";
                MediaGenerator.Generate($"video:{videoId}:{title}", Path.Combine(mediaRoot, "covers", coverName));
                await db.ExecuteAsync("UPDATE videos SET cover_url = ? WHERE id = ?", $"/media/covers/{coverName}", videoId);
            }
            var createdVideo = await GetVideo(db, videoId)
                ?? throw new InvalidOperationException("已创建的视频无法重新读取");
            return Json(new { video = (await SerializeVideos(db, [createdVideo], null)).Single() }, 201);
        }
        catch
        {
            RemoveMediaFile(mediaRoot, $"/media/videos/{videoName}");
            if (coverName is not null)
                RemoveMediaFile(mediaRoot, $"/media/covers/{coverName}");
            throw;
        }
    }

    [HttpDelete("videos/{id:long}")]
    public async Task<IResult> DeleteVideo(long id)
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        var row = await GetVideo(db, id);
        if (row is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        if (Number(row, "user_id") != Number(viewer, "id"))
            throw new ApiException(403, "FORBIDDEN", "只能删除自己的作品");
        await db.ExecuteAsync("DELETE FROM videos WHERE id = ?", id);
        RemoveMediaFile(mediaRoot, String(row, "video_url"));
        RemoveMediaFile(mediaRoot, String(row, "cover_url"));
        return Json(new { ok = true });
    }

    [HttpPost("videos/{id:long}/like")]
    public async Task<IResult> LikeVideo(long id)
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        if (await GetVideo(db, id) is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        await db.TransactionAsync(async (connection, transaction) =>
        {
            var changes = await DataStore.ExecuteAsync(connection, transaction,
                "INSERT INTO likes (user_id, video_id, created_at) VALUES (?, ?, ?) ON CONFLICT DO NOTHING",
                Number(viewer, "id"), id, db.Timestamp(DateTimeOffset.UtcNow));
            if (changes > 0)
                await DataStore.ExecuteAsync(connection, transaction, "UPDATE videos SET like_count = like_count + 1 WHERE id = ?", id);
            return true;
        });
        var count = await db.FirstAsync("SELECT like_count FROM videos WHERE id = ?", id);
        return Json(new { liked = true, likeCount = Number(count!, "like_count") });
    }

    [HttpDelete("videos/{id:long}/like")]
    public async Task<IResult> UnlikeVideo(long id)
    {
        var context = HttpContext;

        var viewer = await RequireUser(context, db, auth);
        if (await GetVideo(db, id) is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        await db.TransactionAsync(async (connection, transaction) =>
        {
            var changes = await DataStore.ExecuteAsync(connection, transaction,
                "DELETE FROM likes WHERE user_id = ? AND video_id = ?", Number(viewer, "id"), id);
            if (changes > 0)
                await DataStore.ExecuteAsync(connection, transaction,
                    "UPDATE videos SET like_count = CASE WHEN like_count > 0 THEN like_count - 1 ELSE 0 END WHERE id = ?", id);
            return true;
        });
        var count = await db.FirstAsync("SELECT like_count FROM videos WHERE id = ?", id);
        return Json(new { liked = false, likeCount = Number(count!, "like_count") });
    }

    [HttpPost("videos/{id:long}/view")]
    public async Task<IResult> RecordView(long id)
    {
        var context = HttpContext;

        if (await GetVideo(db, id) is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        var viewer = await OptionalUser(context, db, auth);
        await db.TransactionAsync(async (connection, transaction) =>
        {
            await DataStore.ExecuteAsync(connection, transaction, "UPDATE videos SET view_count = view_count + 1 WHERE id = ?", id);
            if (viewer is not null)
                await DataStore.ExecuteAsync(connection, transaction,
                    "INSERT INTO video_views (user_id, video_id, viewed_at) VALUES (?, ?, ?) ON CONFLICT(user_id, video_id) DO UPDATE SET viewed_at = excluded.viewed_at",
                    Number(viewer, "id"), id, db.Timestamp(DateTimeOffset.UtcNow));
            return true;
        });
        var count = await db.FirstAsync("SELECT view_count FROM videos WHERE id = ?", id);
        return Json(new { viewCount = Number(count!, "view_count") });
    }

    [HttpPost("videos/{id:long}/share")]
    public async Task<IResult> ShareVideo(long id)
    {
        var context = HttpContext;

        if (await GetVideo(db, id) is null)
            throw new ApiException(404, "NOT_FOUND", "视频不存在");
        await db.ExecuteAsync("UPDATE videos SET share_count = share_count + 1 WHERE id = ?", id);
        var count = await db.FirstAsync("SELECT share_count FROM videos WHERE id = ?", id);
        return Json(new { shareCount = Number(count!, "share_count") });
    }
}
