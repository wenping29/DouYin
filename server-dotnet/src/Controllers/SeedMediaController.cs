using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace DouYing.Server.Controllers;

[ApiController]
[Route("media/seed")]
public sealed class SeedMediaController(BackendPaths paths) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    [HttpGet("{**path}")]
    public IResult Get(string path)
    {
        var fullPath = ApiControllerBase.SafeChildPath(paths.SeedRoot, path);
        if (fullPath is null || !System.IO.File.Exists(fullPath))
            return Results.NotFound();
        if (!ContentTypes.TryGetContentType(fullPath, out var contentType))
            contentType = "application/octet-stream";
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.CacheControl = "public, max-age=3600";
        return Results.File(fullPath, contentType, enableRangeProcessing: true);
    }
}
