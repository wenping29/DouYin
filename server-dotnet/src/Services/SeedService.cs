namespace DouYing.Server;

public sealed class SeedService(DataStore db, string mediaRoot, string legacySeedRoot, AuthService auth)
{
    private sealed record DemoUser(string Username, string Nickname, string Bio, string Seed);
    private sealed record DemoVideo(
        string File, string Title, string Description, string Username, double Duration,
        long Views, long Likes, long Shares);
    private sealed record DemoComment(int Video, string Username, string Content, int Likes);

    private static readonly DemoUser[] Users =
    [
        new("aze", "阿泽", "用镜头记录城市和夜晚 🌃", "aze-night"),
        new("xiaoman", "小满", "今天也要好好吃饭 🍚", "xiaoman-food"),
        new("lulu", "鹿鹿", "旅行 | 摄影 | 咖啡 ☕️", "lulu-travel"),
        new("shanju", "山居厨房", "一人食也要认真 🍳", "shanju-kitchen")
    ];

    private static readonly DemoVideo[] Videos =
    [
        new("demo1.mp4", "下班后的快乐，就是这么简单 🐰", "把今天的疲惫留在路上，回家只负责开心。#日常 #治愈", "aze", 10, 12800, 2361, 89),
        new("demo2.mp4", "路边的花开了，春天真的来了 🌸", "随手一拍，都是春天写给人间的信。#春天 #随手拍", "xiaoman", 14, 8300, 1204, 45),
        new("demo3.mp4", "把周末交给一场说走就走的旅行 ✈️", "目的地不重要，出发本身就很浪漫。#旅行 #在路上", "lulu", 52, 21000, 4320, 210),
        new("demo4.mp4", "一个人也要好好吃饭 🍳", "认真做饭的人，运气都不会太差。#一人食 #厨房", "shanju", 33, 5600, 880, 32)
    ];

    private static readonly DemoComment[] Comments =
    [
        new(0, "xiaoman", "这也太治愈了吧，循环第三遍 🥹", 12),
        new(0, "lulu", "求 BGM！", 3),
        new(1, "aze", "春天真的来了，周末去走走 🌸", 5),
        new(2, "shanju", "已加入旅行清单", 8),
        new(3, "xiaoman", "一个人也要好好吃饭，赞同！", 6)
    ];

    public async Task SeedIfEmptyAsync()
    {
        var count = await db.FirstAsync("SELECT COUNT(*) AS c FROM users");
        if (Convert.ToInt64(count!["c"]) > 0)
            return;

        var userIds = new Dictionary<string, long>();
        for (var i = 0; i < Users.Length; i++)
        {
            var user = Users[i];
            var avatarName = $"u{i + 1}-seed.png";
            MediaGenerator.Generate($"{user.Seed}:{user.Username}", Path.Combine(mediaRoot, "avatars", avatarName), true);
            userIds[user.Username] = await db.InsertIdAsync(
                "INSERT INTO users (username, password_hash, nickname, avatar_url, bio, created_at) VALUES (?, ?, ?, ?, ?, ?) RETURNING id",
                user.Username, auth.HashPassword("demo1234"), user.Nickname, $"/media/avatars/{avatarName}", user.Bio,
                db.Timestamp(DateTimeOffset.UtcNow.AddDays(-(30 - i))));
        }

        var videoIds = new List<long>();
        for (var i = 0; i < Videos.Length; i++)
        {
            var video = Videos[i];
            var filename = File.Exists(Path.Combine(legacySeedRoot, video.File)) ? video.File : "";
            var videoUrl = filename.Length > 0
                ? $"/media/seed/{filename}"
                : "https://www.w3schools.com/html/mov_bbb.mp4";
            var id = await db.InsertIdAsync(
                """
                INSERT INTO videos (user_id, title, description, video_url, cover_url, duration, view_count, like_count, comment_count, share_count, created_at)
                VALUES (?, ?, ?, ?, NULL, ?, ?, ?, 0, ?, ?) RETURNING id
                """,
                userIds[video.Username], video.Title, video.Description, videoUrl, video.Duration,
                video.Views, video.Likes, video.Shares, db.Timestamp(DateTimeOffset.UtcNow.AddHours(-(i + 1) * 5)));
            videoIds.Add(id);
            var cover = $"v{id}.png";
            MediaGenerator.Generate($"video:{id}:{video.Title}", Path.Combine(mediaRoot, "covers", cover));
            await db.ExecuteAsync("UPDATE videos SET cover_url = ? WHERE id = ?", $"/media/covers/{cover}", id);
        }

        for (var i = 0; i < Comments.Length; i++)
        {
            var comment = Comments[i];
            await db.ExecuteAsync(
                "INSERT INTO comments (video_id, user_id, content, like_count, created_at) VALUES (?, ?, ?, ?, ?)",
                videoIds[comment.Video], userIds[comment.Username], comment.Content, comment.Likes,
                db.Timestamp(DateTimeOffset.UtcNow.AddMinutes(-(i + 1) * 30)));
        }
        await db.ExecuteAsync("UPDATE videos SET comment_count = (SELECT COUNT(*) FROM comments WHERE comments.video_id = videos.id)");

        var now = db.Timestamp(DateTimeOffset.UtcNow);
        await db.ExecuteAsync("INSERT INTO likes (user_id, video_id, created_at) VALUES (?, ?, ?) ON CONFLICT DO NOTHING", userIds["aze"], videoIds[1], now);
        await db.ExecuteAsync("INSERT INTO likes (user_id, video_id, created_at) VALUES (?, ?, ?) ON CONFLICT DO NOTHING", userIds["xiaoman"], videoIds[0], now);
        await db.ExecuteAsync("INSERT INTO likes (user_id, video_id, created_at) VALUES (?, ?, ?) ON CONFLICT DO NOTHING", userIds["lulu"], videoIds[0], now);
        await db.ExecuteAsync("INSERT INTO likes (user_id, video_id, created_at) VALUES (?, ?, ?) ON CONFLICT DO NOTHING", userIds["shanju"], videoIds[2], now);

        var followPairs = new (string Follower, string Following)[]
        {
            ("aze", "xiaoman"), ("aze", "lulu"), ("xiaoman", "aze"),
            ("lulu", "aze"), ("lulu", "shanju"), ("shanju", "aze"), ("shanju", "xiaoman")
        };
        foreach (var (follower, following) in followPairs)
            await db.ExecuteAsync(
                "INSERT INTO follows (follower_id, following_id, created_at) VALUES (?, ?, ?) ON CONFLICT DO NOTHING",
                userIds[follower], userIds[following], now);

        await db.ExecuteAsync(
            "INSERT INTO video_views (user_id, video_id, viewed_at) VALUES (?, ?, ?) ON CONFLICT(user_id, video_id) DO UPDATE SET viewed_at = excluded.viewed_at",
            userIds["aze"], videoIds[1], now);
        await db.ExecuteAsync(
            "INSERT INTO video_views (user_id, video_id, viewed_at) VALUES (?, ?, ?) ON CONFLICT(user_id, video_id) DO UPDATE SET viewed_at = excluded.viewed_at",
            userIds["aze"], videoIds[2], now);
    }
}
