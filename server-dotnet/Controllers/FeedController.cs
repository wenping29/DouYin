using Microsoft.AspNetCore.Mvc;

namespace DouYing.Server.Controllers;

public sealed class FeedController(DataStore db, AuthService auth, BackendPaths paths)
    : ApiControllerBase(db, auth, paths)
{
    [HttpGet("feed")]
    public async Task<IResult> GetFeed()
    {
        var context = HttpContext;

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
    }
}
