using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using DouYing.Server;
using DouYing.Server.Controllers;

var projectRoot = FindProjectRoot();
LoadDotEnv(Path.Combine(projectRoot, ".env"));

var builder = WebApplication.CreateBuilder(args);
var port = ReadInt("PORT", 8080);
var mediaRoot = Path.Combine(projectRoot, "media");
var seedRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "server", "media", "seed"));
foreach (var folder in new[] { "videos", "covers", "avatars" })
    Directory.CreateDirectory(Path.Combine(mediaRoot, folder));

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 210L * 1024 * 1024);
builder.Services.AddCors();
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 210L * 1024 * 1024;
    options.ValueLengthLimit = 1024 * 1024;
});

var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
if (string.IsNullOrEmpty(secret))
    secret = "douying-dev-secret-change-me";
builder.Services.AddSingleton<DataStore>(_ => new DataStore(projectRoot));
builder.Services.AddSingleton<AuthService>(_ => new AuthService(secret));
builder.Services.AddSingleton(new BackendPaths(mediaRoot));
builder.Services.AddSingleton<BackendRuntime>();

var app = builder.Build();
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length"));
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ApiException exception)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = exception.Status;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = exception.Code, message = exception.Message }
            });
        }
    }
    catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "FILE_TOO_LARGE", message = "文件超过大小限制" }
            });
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"[error] {exception}");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "INTERNAL_ERROR", message = "服务端开小差了，请稍后重试" }
            });
        }
    }
});

var mediaProvider = new FileExtensionContentTypeProvider();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(mediaRoot),
    RequestPath = "/media",
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers["Accept-Ranges"] = "bytes";
        context.Context.Response.Headers["Cache-Control"] = "public, max-age=3600";
    },
    ContentTypeProvider = mediaProvider
});

var database = app.Services.GetRequiredService<DataStore>();
try
{
    await database.InitializeAsync();
    var auth = app.Services.GetRequiredService<AuthService>();
    await new SeedService(database, mediaRoot, seedRoot, auth).SeedIfEmptyAsync();
    app.Services.GetRequiredService<BackendRuntime>().StartedAt = Environment.TickCount64;
    app.MapControllers();
    MediaEndpoints.Map(app, seedRoot);
    app.MapFallback("/api/{**path}", (HttpContext context) =>
        Results.Json(new
        {
            error = new
            {
                code = "NOT_FOUND",
                message = $"接口不存在：{context.Request.Method} {context.Request.Path}"
            }
        }, statusCode: StatusCodes.Status404NotFound));
    Console.WriteLine($"\n抖影 C# API 已启动: http://localhost:{port}");
    Console.WriteLine($"健康检查: http://localhost:{port}/api/health");
    Console.WriteLine($"数据库: {(database.Dialect == "postgres" ? "PostgreSQL" : "SQLite")}");
    Console.WriteLine("演示账号: aze / xiaoman / lulu / shanju，密码 demo1234\n");
    await app.RunAsync();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"[startup] 启动失败: {exception}");
    Environment.ExitCode = 1;
}

static string FindProjectRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "DouYing.Server.csproj")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("无法定位 server-dotnet 项目目录");
}

static void LoadDotEnv(string path)
{
    if (!File.Exists(path))
        return;
    foreach (var line in File.ReadLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            continue;
        var separator = trimmed.IndexOf('=');
        if (separator <= 0)
            continue;
        var key = trimmed[..separator].Trim();
        var value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, value);
    }
}

static int ReadInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
