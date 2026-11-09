using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;

namespace DouYing.Server;

public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static partial class ApiEndpoints
{
    private const long MaxVideoBytes = 200L * 1024 * 1024;
    private const long MaxImageBytes = 5L * 1024 * 1024;
    private const string UserSelect = """
        SELECT u.*,
          (SELECT COUNT(*) FROM follows f WHERE f.following_id = u.id) AS follower_count,
          (SELECT COUNT(*) FROM follows f WHERE f.follower_id = u.id) AS following_count,
          (SELECT COUNT(*) FROM videos v WHERE v.user_id = u.id AND v.status = 'published') AS video_count
        FROM users u
        """;
    private const string VideoSelect = """
        SELECT v.*, u.username AS author_username, u.nickname AS author_nickname,
               u.avatar_url AS author_avatar
        FROM videos v JOIN users u ON u.id = v.user_id
        """;
    private const string CommentSelect = """
        SELECT c.*, u.username AS author_username, u.nickname AS author_nickname,
               u.avatar_url AS author_avatar
        FROM comments c JOIN users u ON u.id = c.user_id
        """;

    public static void Map(WebApplication app, DataStore db, AuthService auth, string mediaRoot,
        string seedRoot, long startedAt, string dialect)
    {
        app.MapGet("/api/health", () => Results.Json(new
        {
            ok = true,
            uptime = (Environment.TickCount64 - startedAt) / 1000d,
            database = dialect
        }));

        app.MapPost("/api/auth/register", async (HttpContext context) =>
        {
            var body = await ReadJson(context);
            var username = (Property(body, "username")?.GetString() ?? "").Trim().ToLowerInvariant();
            var password = Property(body, "password")?.GetString() ?? "";
            var nickname = Text(Property(body, "nickname"), 20);
            if (!UsernamePattern().IsMatch(username))
                throw new ApiException(400, "VALIDATION_ERROR", "用户名需为 3-20 位字母、数字或下划线");
            if (password.Length is < 6 or > 64)
                throw new ApiException(400, "VALIDATION_ERROR", "密码长度需为 6-64 位");
            if (await db.FirstAsync("SELECT id FROM users WHERE username = ?", username) is not null)
                throw new ApiException(409, "USERNAME_TAKEN", "用户名已被占用");

            var avatarName = $"u-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.png";
            MediaGenerator.Generate($"{username}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                Path.Combine(mediaRoot, "avatars", avatarName), true);
            var avatarUrl = $"/media/avatars/{avatarName}";
            var id = await db.InsertIdAsync(
                "INSERT INTO users (username, password_hash, nickname, avatar_url, bio, created_at) VALUES (?, ?, ?, ?, ?, ?) RETURNING id",
                username, auth.HashPassword(password), nickname.Length == 0 ? username : nickname,
                avatarUrl, "", db.Timestamp(DateTimeOffset.UtcNow));
            return Json(new { token = auth.CreateToken(id), user = PublicUser(await GetUser(db, id)) }, 201);
        });

        app.MapPost("/api/auth/login", async (HttpContext context) =>
        {
            var body = await ReadJson(context);
            var username = Text(Property(body, "username"), 64).ToLowerInvariant();
            var password = Property(body, "password")?.GetString() ?? "";
            var row = await db.FirstAsync("SELECT * FROM users WHERE username = ?", username);
            if (row is null || !auth.VerifyPassword(password, String(row, "password_hash")))
                throw new ApiException(401, "INVALID_CREDENTIALS", "用户名或密码错误");
            var id = Number(row, "id");
            return Json(new { token = auth.CreateToken(id), user = PublicUser(await GetUser(db, id)) });
        });

        app.MapGet("/api/me", async (HttpContext context) =>
        {
            var user = await RequireUser(context, db, auth);
            return Json(new { user = PublicUser(user) });
        });

        app.MapPatch("/api/me", async (HttpContext context) =>
        {
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
        });

        app.MapPost("/api/me/avatar", async (HttpContext context) =>
        {
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
        }).DisableAntiforgery();

        app.MapGet("/api/users/suggested", async (HttpContext context) =>
        {
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
        });

        app.MapGet("/api/users/{id:long}", async (long id, HttpContext context) =>
        {
            var row = await GetUser(db, id);
            if (row is null)
                throw new ApiException(404, "NOT_FOUND", "用户不存在");
            var viewer = await OptionalUser(context, db, auth);
            var following = viewer is not null && Number(viewer, "id") != id &&
                await db.FirstAsync("SELECT 1 AS ok FROM follows WHERE follower_id = ? AND following_id = ?",
                    Number(viewer, "id"), id) is not null;
            return Json(new { user = PublicUser(row, following) });
        });

        app.MapGet("/api/users/{id:long}/videos", async (long id, HttpContext context) =>
        {
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
        });

        app.MapPost("/api/users/{id:long}/follow", async (long id, HttpContext context) =>
        {
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
        });

        app.MapDelete("/api/users/{id:long}/follow", async (long id, HttpContext context) =>
        {
            var viewer = await RequireUser(context, db, auth);
            await db.ExecuteAsync("DELETE FROM follows WHERE follower_id = ? AND following_id = ?", Number(viewer, "id"), id);
            var count = await db.FirstAsync("SELECT COUNT(*) AS c FROM follows WHERE following_id = ?", id);
            return Json(new { following = false, followerCount = Number(count!, "c") });
        });

        app.MapGet("/api/feed", async (HttpContext context) =>
        {
            var viewer = await OptionalUser(context, db, auth);
            var tab = context.Request.Query["tab"] == "following" ? "following" : "recommend";
            var (limit, offset) = Paging(context, 10, 30);
            if (tab == "following")
            {
                if (viewer is null)
                    throw new ApiException(401, "UNAUTHORIZED", "关注流需要先登录");
                var followingRows = await db.QueryAsync($"""
                    {VideoSelect} JOIN follows f ON f.following_id = v.user_id
                    WHERE f.follower_id = ? AND v.status = 'published'
                    ORDER BY v.created_at DESC, v.id DESC LIMIT ? OFFSET ?
                    """, Number(viewer, "id"), limit + 1, offset);
                return Json(await VideoPage(db, followingRows, limit, offset, viewer));
            }

            var candidates = await db.QueryAsync(
                $"{VideoSelect} WHERE v.status = 'published' ORDER BY v.created_at DESC LIMIT 500");
            if (viewer is not null)
                candidates = candidates.Where(row => Number(row, "user_id") != Number(viewer, "id")).ToList();
            var sorted = candidates.OrderByDescending(RecommendScore).ThenByDescending(row => Number(row, "id")).ToList();
            var page = sorted.Skip(offset).Take(limit + 1).ToList();
            return Json(await VideoPage(db, page, limit, offset, viewer));
        });

        app.MapGet("/api/videos/{id:long}", async (long id, HttpContext context) =>
        {
            var row = await GetVideo(db, id);
            if (row is null)
                throw new ApiException(404, "NOT_FOUND", "视频不存在");
            return Json(new { video = (await SerializeVideos(db, [row], await OptionalUser(context, db, auth))).Single() });
        });

        app.MapPost("/api/videos", async (HttpContext context) =>
        {
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
                _ = double.TryParse(form["duration"].FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
                var videoId = await db.InsertIdAsync(
                    """
                    INSERT INTO videos (user_id, title, description, video_url, cover_url, duration, created_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?) RETURNING id
                    """,
                    Number(viewer, "id"), title, description, $"/media/videos/{videoName}",
                    coverName is null ? null : $"/media/covers/{coverName}", Math.Max(0, duration), db.Timestamp(DateTimeOffset.UtcNow));
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
        }).DisableAntiforgery();

        app.MapDelete("/api/videos/{id:long}", async (long id, HttpContext context) =>
        {
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
        });

        app.MapPost("/api/videos/{id:long}/like", async (long id, HttpContext context) =>
        {
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
        });

        app.MapDelete("/api/videos/{id:long}/like", async (long id, HttpContext context) =>
        {
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
        });

        app.MapPost("/api/videos/{id:long}/view", async (long id, HttpContext context) =>
        {
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
        });

        app.MapPost("/api/videos/{id:long}/share", async (long id) =>
        {
            if (await GetVideo(db, id) is null)
                throw new ApiException(404, "NOT_FOUND", "视频不存在");
            await db.ExecuteAsync("UPDATE videos SET share_count = share_count + 1 WHERE id = ?", id);
            var count = await db.FirstAsync("SELECT share_count FROM videos WHERE id = ?", id);
            return Json(new { shareCount = Number(count!, "share_count") });
        });

        app.MapGet("/api/videos/{id:long}/comments", async (long id, HttpContext context) =>
        {
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
        });

        app.MapPost("/api/videos/{id:long}/comments", async (long id, HttpContext context) =>
        {
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
        });

        app.MapDelete("/api/comments/{id:long}", async (long id, HttpContext context) =>
        {
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
            var count = await db.TransactionAsync(async (connection, transaction) =>
            {
                await DataStore.ExecuteAsync(connection, transaction, "DELETE FROM comments WHERE id = ?", id);
                await DataStore.ExecuteAsync(connection, transaction,
                    "UPDATE videos SET comment_count = CASE WHEN comment_count > 0 THEN comment_count - 1 ELSE 0 END WHERE id = ?",
                    Number(comment, "video_id"));
                return true;
            });
            _ = count;
            var updated = await db.FirstAsync("SELECT comment_count FROM videos WHERE id = ?", Number(comment, "video_id"));
            return Json(new { ok = true, commentCount = Number(updated!, "comment_count") });
        });

        app.MapGet("/api/search", async (HttpContext context) =>
        {
            var viewer = await OptionalUser(context, db, auth);
            var query = (context.Request.Query["q"].FirstOrDefault() ?? "").Trim();
            if (query.Length > 50)
                query = query[..50];
            var type = context.Request.Query["type"].FirstOrDefault();
            if (type is not ("all" or "videos" or "users"))
                type = "all";
            if (query.Length == 0)
                return Json(new { videos = Array.Empty<object>(), users = Array.Empty<object>() });

            var pattern = $"%{EscapeLike(query)}%";
            var videos = new List<object>();
            var users = new List<object>();
            if (type is "all" or "videos")
            {
                var rows = await db.QueryAsync($"""
                    {VideoSelect}
                    WHERE v.status = 'published' AND
                      (LOWER(v.title) LIKE LOWER(?) ESCAPE '\' OR LOWER(v.description) LIKE LOWER(?) ESCAPE '\')
                    ORDER BY v.like_count DESC, v.id DESC LIMIT 20
                    """, pattern, pattern);
                videos = await SerializeVideos(db, rows, viewer);
            }
            if (type is "all" or "users")
            {
                var rows = await db.QueryAsync($"""
                    {UserSelect}
                    WHERE LOWER(u.nickname) LIKE LOWER(?) ESCAPE '\' OR LOWER(u.username) LIKE LOWER(?) ESCAPE '\'
                    ORDER BY follower_count DESC, u.id DESC LIMIT 20
                    """, pattern, pattern);
                var following = await FollowingIds(db, viewer, rows.Select(row => Number(row, "id")).ToList());
                users = rows.Select(row => PublicUser(row, following.Contains(Number(row, "id")))).ToList();
            }
            return Json(new { videos, users });
        });

        app.MapGet("/media/seed/{**path}", async (string path, HttpContext context) =>
        {
            var fullPath = SafeChildPath(seedRoot, path);
            if (fullPath is null || !File.Exists(fullPath))
                return Results.NotFound();
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fullPath, out var contentType))
                contentType = "application/octet-stream";
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.File(fullPath, contentType, enableRangeProcessing: true);
        });

        app.MapFallback("/api/{**path}", (HttpContext context) =>
            Json(new
            {
                error = new
                {
                    code = "NOT_FOUND",
                    message = $"接口不存在：{context.Request.Method} {context.Request.Path}"
                }
            }, 404));
    }

    private static async Task<object> VideoPage(
        DataStore db, List<Dictionary<string, object?>> rows, int limit, int offset,
        Dictionary<string, object?>? viewer)
    {
        var hasMore = rows.Count > limit;
        if (hasMore)
            rows = rows.Take(limit).ToList();
        return new
        {
            items = await SerializeVideos(db, rows, viewer),
            nextCursor = hasMore ? (string?)(offset + limit).ToString(CultureInfo.InvariantCulture) : null
        };
    }

    private static async Task<List<object>> SerializeVideos(
        DataStore db, List<Dictionary<string, object?>> rows, Dictionary<string, object?>? viewer)
    {
        long? viewerId = viewer is null ? null : Number(viewer, "id");
        var ids = rows.Select(row => Number(row, "id")).Distinct().ToList();
        var authorIds = rows.Select(row => Number(row, "user_id")).Distinct().ToList();
        var liked = new HashSet<long>();
        var following = new HashSet<long>();
        if (viewerId is not null && ids.Count > 0)
        {
            var placeholders = string.Join(",", Enumerable.Repeat("?", ids.Count));
            var args = new object?[] { viewerId }.Concat(ids.Cast<object?>()).ToArray();
            foreach (var row in await db.QueryAsync($"SELECT video_id FROM likes WHERE user_id = ? AND video_id IN ({placeholders})", args))
                liked.Add(Number(row, "video_id"));
            placeholders = string.Join(",", Enumerable.Repeat("?", authorIds.Count));
            args = new object?[] { viewerId }.Concat(authorIds.Cast<object?>()).ToArray();
            foreach (var row in await db.QueryAsync($"SELECT following_id FROM follows WHERE follower_id = ? AND following_id IN ({placeholders})", args))
                following.Add(Number(row, "following_id"));
        }

        return rows.Select(row => (object)new
        {
            id = Number(row, "id"),
            title = String(row, "title"),
            description = String(row, "description"),
            videoUrl = String(row, "video_url"),
            coverUrl = row.GetValueOrDefault("cover_url"),
            duration = Double(row, "duration"),
            width = Number(row, "width"),
            height = Number(row, "height"),
            viewCount = Number(row, "view_count"),
            likeCount = Number(row, "like_count"),
            commentCount = Number(row, "comment_count"),
            shareCount = Number(row, "share_count"),
            createdAt = String(row, "created_at"),
            liked = liked.Contains(Number(row, "id")),
            author = new
            {
                id = Number(row, "user_id"),
                username = String(row, "author_username"),
                nickname = String(row, "author_nickname"),
                avatarUrl = row.GetValueOrDefault("author_avatar"),
                isFollowing = following.Contains(Number(row, "user_id"))
            }
        }).ToList();
    }

    private static object PublicUser(Dictionary<string, object?>? row, bool isFollowing = false)
    {
        if (row is null)
            return new { };
        return new
        {
            id = Number(row, "id"),
            username = String(row, "username"),
            nickname = String(row, "nickname"),
            avatarUrl = row.GetValueOrDefault("avatar_url"),
            bio = String(row, "bio"),
            followerCount = Number(row, "follower_count"),
            followingCount = Number(row, "following_count"),
            videoCount = Number(row, "video_count"),
            isFollowing,
            createdAt = String(row, "created_at")
        };
    }

    private static object PublicComment(
        Dictionary<string, object?> row, long? viewerId, long videoOwnerId) => new
    {
        id = Number(row, "id"),
        content = String(row, "content"),
        likeCount = Number(row, "like_count"),
        createdAt = String(row, "created_at"),
        isMine = viewerId is not null && Number(row, "user_id") == viewerId,
        canDelete = viewerId is not null && (Number(row, "user_id") == viewerId || videoOwnerId == viewerId),
        author = new
        {
            id = Number(row, "user_id"),
            username = String(row, "author_username"),
            nickname = String(row, "author_nickname"),
            avatarUrl = row.GetValueOrDefault("author_avatar")
        }
    };

    private static async Task<HashSet<long>> FollowingIds(
        DataStore db, Dictionary<string, object?>? viewer, List<long> ids)
    {
        var result = new HashSet<long>();
        if (viewer is null || ids.Count == 0)
            return result;
        var placeholders = string.Join(",", Enumerable.Repeat("?", ids.Count));
        var args = new object?[] { Number(viewer, "id") }.Concat(ids.Cast<object?>()).ToArray();
        foreach (var row in await db.QueryAsync(
                     $"SELECT following_id FROM follows WHERE follower_id = ? AND following_id IN ({placeholders})", args))
            result.Add(Number(row, "following_id"));
        return result;
    }

    private static async Task<Dictionary<string, object?>?> GetUser(DataStore db, long id) =>
        await db.FirstAsync($"{UserSelect} WHERE u.id = ?", id);

    private static async Task<Dictionary<string, object?>?> GetVideo(DataStore db, long id) =>
        await db.FirstAsync($"{VideoSelect} WHERE v.id = ?", id);

    private static async Task<Dictionary<string, object?>> RequireUser(HttpContext context, DataStore db, AuthService auth) =>
        await OptionalUser(context, db, auth) ??
        throw new ApiException(401, "UNAUTHORIZED", "请先登录");

    private static async Task<Dictionary<string, object?>?> OptionalUser(HttpContext context, DataStore db, AuthService auth)
    {
        var id = auth.ReadToken(context.Request.Headers.Authorization.FirstOrDefault());
        return id is null ? null : await GetUser(db, id.Value);
    }

    private static async Task<IFormCollection> ReadForm(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
            throw new ApiException(400, "VALIDATION_ERROR", "请求必须使用 multipart/form-data");
        return await context.Request.ReadFormAsync();
    }

    private static async Task<JsonElement> ReadJson(HttpContext context)
    {
        if (!context.Request.HasJsonContentType())
            throw new ApiException(400, "VALIDATION_ERROR", "请求体不是合法 JSON");
        try
        {
            return await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            throw new ApiException(400, "VALIDATION_ERROR", "请求体不是合法 JSON");
        }
    }

    private static JsonElement? Property(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) ? value : null;

    private static bool HasProperty(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out _);

    private static string Text(JsonElement? element, int maxLength)
    {
        var value = element?.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number => element.Value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
        return Text(value, maxLength);
    }

    private static string Text(string? value, int maxLength) =>
        (value ?? "").Trim() is var trimmed && trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;

    private static void ValidateFile(IFormFile file, bool video, long maxBytes)
    {
        if (file.Length > maxBytes)
            throw new ApiException(413, "FILE_TOO_LARGE", "文件超过大小限制");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var accepted = video
            ? new[] { ".mp4", ".mov", ".m4v", ".webm", ".mkv" }
            : new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        var mimeMatches = file.ContentType.StartsWith(video ? "video/" : "image/", StringComparison.OrdinalIgnoreCase);
        if (!mimeMatches && !accepted.Contains(extension))
            throw new ApiException(400, video ? "INVALID_VIDEO_TYPE" : "INVALID_IMAGE_TYPE",
                video ? "仅支持视频文件" : "仅支持图片文件");
    }

    private static string NewFileName(string originalName)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}{extension}";
    }

    private static async Task SaveFile(IFormFile file, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = File.Create(path);
        await file.CopyToAsync(target);
    }

    private static void RemoveMediaFile(string root, string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("/media/", StringComparison.Ordinal))
            return;
        var full = SafeChildPath(root, url["/media/".Length..]);
        if (full is not null && File.Exists(full))
            File.Delete(full);
    }

    private static string? SafeChildPath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static (int Limit, int Offset) Paging(HttpContext context, int defaultLimit, int maxLimit)
    {
        var limit = QueryInt(context, "limit", defaultLimit);
        var offset = QueryInt(context, "cursor", 0);
        return (Math.Clamp(limit, 1, maxLimit), Math.Max(0, offset));
    }

    private static int QueryInt(HttpContext context, string name, int fallback) =>
        int.TryParse(context.Request.Query[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : fallback;

    private static double RecommendScore(Dictionary<string, object?> row)
    {
        var created = DateTimeOffset.TryParse(String(row, "created_at"), out var date) ? date : DateTimeOffset.UtcNow;
        var ageHours = Math.Max(0, (DateTimeOffset.UtcNow - created).TotalHours);
        var engagement = Number(row, "like_count") * 3d + Number(row, "comment_count") * 5d +
                         Number(row, "share_count") * 4d + Number(row, "view_count") * 0.1d + 2;
        return engagement / Math.Pow(ageHours + 2, 0.55);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static long Number(Dictionary<string, object?> row, string key) =>
        Convert.ToInt64(row.GetValueOrDefault(key) ?? 0, CultureInfo.InvariantCulture);

    private static double Double(Dictionary<string, object?> row, string key) =>
        Convert.ToDouble(row.GetValueOrDefault(key) ?? 0, CultureInfo.InvariantCulture);

    private static string String(Dictionary<string, object?> row, string key) =>
        Convert.ToString(row.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "";

    private static IResult Json(object value, int status = 200) =>
        Results.Json(value, statusCode: status);

    [GeneratedRegex("^[a-zA-Z0-9_]{3,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
