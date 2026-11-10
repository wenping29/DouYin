using Microsoft.AspNetCore.Mvc;
using DouYing.Server.Domain;

namespace DouYing.Server.Controllers;

public sealed class AuthController(DataStore db, AuthService auth, BackendPaths paths)
    : ApiControllerBase(db, auth, paths)
{
    [HttpPost("auth/register")]
    public async Task<IResult> Register()
    {
        var context = HttpContext;

        var body = await ReadJson(context);
        var username = UserPolicy.NormalizeUsername(Property(body, "username")?.GetString());
        var password = Property(body, "password")?.GetString() ?? "";
        var nickname = Text(Property(body, "nickname"), 20);
        if (!UserPolicy.IsValidUsername(username))
            throw new ApiException(400, "VALIDATION_ERROR", "用户名需为 3-20 位字母、数字或下划线");
        if (!UserPolicy.IsValidPassword(password))
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
    }

    [HttpPost("auth/login")]
    public async Task<IResult> Login()
    {
        var context = HttpContext;

        var body = await ReadJson(context);
        var username = UserPolicy.NormalizeUsername(Text(Property(body, "username"), 64));
        var password = Property(body, "password")?.GetString() ?? "";
        var row = await db.FirstAsync("SELECT * FROM users WHERE username = ?", username);
        if (row is null || !auth.VerifyPassword(password, String(row, "password_hash")))
            throw new ApiException(401, "INVALID_CREDENTIALS", "用户名或密码错误");
        var id = Number(row, "id");
        return Json(new { token = auth.CreateToken(id), user = PublicUser(await GetUser(db, id)) });
    }
}
