using Microsoft.AspNetCore.Mvc;
using Weighing.Server.Api.Data;
using Weighing.Server.Api.Services;

namespace Weighing.Server.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController(SyncService sync) : ControllerBase
{
    private const int MaxBatch = 200;

    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] PushRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ClientId) || req.ClientId.Length > 50 ||
            req.Records is null || req.Records.Count == 0 || req.Records.Count > MaxBatch || req.Records.Any(r => r is null))
            return BadRequest(ApiResponse<PushResponse>.Failed("bad_request", $"Cần ClientId và 1..{MaxBatch} bản ghi."));
        return Ok(ApiResponse<PushResponse>.Succeeded(await sync.PushAsync(req, ct)));
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] long cursor = 0, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (cursor < 0) return BadRequest(ApiResponse<PullResponse>.Failed("bad_request", "cursor phải >= 0."));
        return Ok(ApiResponse<PullResponse>.Succeeded(await sync.PullAsync(cursor, limit, ct)));
    }
}

[ApiController]
[Route("api/health")]
public sealed class HealthController(ServerDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var ok = await db.Database.CanConnectAsync(ct);
        return ok ? Ok(ApiResponse<object>.Succeeded(new { status = "ok" }))
                  : StatusCode(503, ApiResponse<object>.Failed("db_unavailable", "Không kết nối được SQL Server."));
    }
}
