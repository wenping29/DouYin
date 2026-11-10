using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using DouYing.Server.Domain;

namespace DouYing.Server.Controllers;

public sealed class BackendPaths(string mediaRoot, string seedRoot)
{
    public string MediaRoot { get; } = mediaRoot;
    public string SeedRoot { get; } = seedRoot;
}

public sealed class BackendRuntime
{
    public long StartedAt { get; set; }
}

public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

[ApiController]
[Route("api")]
public abstract class ApiControllerBase(DataStore db, AuthService auth, BackendPaths paths) : ControllerBase
{
    protected readonly DataStore db = db;
    protected readonly AuthService auth = auth;
    protected readonly string mediaRoot = paths.MediaRoot;

    protected const long MaxVideoBytes = 200L * 1024 * 1024;
    protected const long MaxImageBytes = 5L * 1024 * 1024;
    protected const string UserSelect = """
        SELECT u.*,
          (SELECT COUNT(*) FROM follows f WHERE f.following_id = u.id) AS follower_count,
          (SELECT COUNT(*) FROM follows f WHERE f.follower_id = u.id) AS following_count,
          (SELECT COUNT(*) FROM videos v WHERE v.user_id = u.id AND v.status = 'published') AS video_count
        FROM users u
        """;
    protected const string VideoSelect = """
        SELECT v.*, u.username AS author_username, u.nickname AS author_nickname,
               u.avatar_url AS author_avatar
        FROM videos v JOIN users u ON u.id = v.user_id
        """;
    protected const string CommentSelect = """
        SELECT c.*, u.username AS author_username, u.nickname AS author_nickname,
               u.avatar_url AS author_avatar
        FROM comments c JOIN users u ON u.id = c.user_id
        """;

    protected static async Task<object> VideoPage(
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

    protected static async Task<List<object>> SerializeVideos(
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

    protected static object PublicUser(Dictionary<string, object?>? row, bool isFollowing = false)
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

    protected static object PublicComment(
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

    protected static async Task<HashSet<long>> FollowingIds(
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

    protected static async Task<Dictionary<string, object?>?> GetUser(DataStore db, long id) =>
        await db.FirstAsync($"{UserSelect} WHERE u.id = ?", id);

    protected static async Task<Dictionary<string, object?>?> GetVideo(DataStore db, long id) =>
        await db.FirstAsync($"{VideoSelect} WHERE v.id = ?", id);

    protected static async Task<Dictionary<string, object?>> RequireUser(HttpContext context, DataStore db, AuthService auth) =>
        await OptionalUser(context, db, auth) ??
        throw new ApiException(401, "UNAUTHORIZED", "请先登录");

    protected static async Task<Dictionary<string, object?>?> OptionalUser(HttpContext context, DataStore db, AuthService auth)
    {
        var id = auth.ReadToken(context.Request.Headers.Authorization.FirstOrDefault());
        return id is null ? null : await GetUser(db, id.Value);
    }

    protected static async Task<IFormCollection> ReadForm(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
            throw new ApiException(400, "VALIDATION_ERROR", "请求必须使用 multipart/form-data");
        return await context.Request.ReadFormAsync();
    }

    protected static async Task<JsonElement> ReadJson(HttpContext context)
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

    protected static JsonElement? Property(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) ? value : null;

    protected static bool HasProperty(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out _);

    protected static string Text(JsonElement? element, int maxLength)
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

    protected static string Text(string? value, int maxLength) =>
        (value ?? "").Trim() is var trimmed && trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;

    protected static void ValidateFile(IFormFile file, bool video, long maxBytes)
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

    protected static string NewFileName(string originalName)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}{extension}";
    }

    protected static async Task SaveFile(IFormFile file, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = System.IO.File.Create(path);
        await file.CopyToAsync(target);
    }

    protected static void RemoveMediaFile(string root, string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("/media/", StringComparison.Ordinal))
            return;
        var full = SafeChildPath(root, url["/media/".Length..]);
        if (full is not null && System.IO.File.Exists(full))
            System.IO.File.Delete(full);
    }

    internal static string? SafeChildPath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    protected static (int Limit, int Offset) Paging(HttpContext context, int defaultLimit, int maxLimit)
    {
        var limit = QueryInt(context, "limit", defaultLimit);
        var offset = QueryInt(context, "cursor", 0);
        return (Math.Clamp(limit, 1, maxLimit), Math.Max(0, offset));
    }

    protected static int QueryInt(HttpContext context, string name, int fallback) =>
        int.TryParse(context.Request.Query[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : fallback;

    protected static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    protected static long Number(Dictionary<string, object?> row, string key) =>
        Convert.ToInt64(row.GetValueOrDefault(key) ?? 0, CultureInfo.InvariantCulture);

    protected static double Double(Dictionary<string, object?> row, string key) =>
        Convert.ToDouble(row.GetValueOrDefault(key) ?? 0, CultureInfo.InvariantCulture);

    protected static string String(Dictionary<string, object?> row, string key) =>
        Convert.ToString(row.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "";

    protected static IResult Json(object value, int status = 200) =>
        Results.Json(value, statusCode: status);

    protected static double RecommendScore(Dictionary<string, object?> row)
    {
        var now = DateTimeOffset.UtcNow;
        var created = DateTimeOffset.TryParse(String(row, "created_at"), out var date) ? date : now;
        return FeedRecommendation.Score(created, Number(row, "like_count"), Number(row, "comment_count"),
            Number(row, "share_count"), Number(row, "view_count"), now);
    }
}
