using Microsoft.AspNetCore.StaticFiles;

namespace DouYing.Server;

public static class MediaEndpoints
{
    public static void Map(WebApplication app, string seedRoot)
    {
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
    }

    private static string? SafeChildPath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
