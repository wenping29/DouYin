using Microsoft.AspNetCore.Mvc;

namespace DouYing.Server.Controllers;

public sealed class SearchController(DataStore db, AuthService auth, BackendPaths paths)
    : ApiControllerBase(db, auth, paths)
{
    [HttpGet("search")]
    public async Task<IResult> Search()
    {
        var context = HttpContext;

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
    }
}
