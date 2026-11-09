using Microsoft.AspNetCore.Mvc;

namespace DouYing.Server.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(DataStore db, BackendRuntime runtime) : ControllerBase
{
    [HttpGet]
    public IResult Get() => Results.Json(new
    {
        ok = true,
        uptime = (Environment.TickCount64 - runtime.StartedAt) / 1000d,
        database = db.Dialect
    });
}
