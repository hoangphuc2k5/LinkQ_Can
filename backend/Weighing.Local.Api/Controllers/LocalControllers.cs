using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Weighing.Local.Api.Data;
using Weighing.Local.Api.Services;
using Weighing.Local.Api.Sync;

namespace Weighing.Local.Api.Controllers;

[ApiController]
[Route("api/local/weighing")]
public sealed class WeighingController(WeighingService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWeighingRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await service.CreateAsync(req, ct);
            return StatusCode(StatusCodes.Status201Created, ApiResponse<LocalWeighingDto>.Succeeded(dto));
        }
        catch (WeighingValidationException ex)
        {
            return BadRequest(ApiResponse<LocalWeighingDto>.Failed("validation", ex.Message));
        }
    }

    [HttpPost("{id:guid}/second")]
    public async Task<IActionResult> Second(Guid id, [FromBody] SecondWeighRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await service.AddSecondWeighAsync(id, req.SecondWeightKg, ct);
            return Ok(ApiResponse<LocalWeighingDto>.Succeeded(dto));
        }
        catch (WeighingValidationException ex)
        {
            return BadRequest(ApiResponse<LocalWeighingDto>.Failed("validation", ex.Message));
        }
        catch (WeighingNotFoundException ex)
        {
            return NotFound(ApiResponse<LocalWeighingDto>.Failed("not_found", ex.Message));
        }
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var dto = await service.FindAsync(id, ct);
        if (dto is null) return NotFound(ApiResponse<object>.Failed("not_found", "Không tìm thấy phiếu cân."));
        var name = "phieu-can-" + dto.LicensePlate.Replace(' ', '-') + ".pdf";
        return File(WeighingPdf.Create(dto), "application/pdf", name);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse<HistoryResult>.Succeeded(await service.QueryAsync(search, from, to, page, pageSize, ct)));
}

[ApiController]
[Route("api/local/sync")]
public sealed class SyncController(IDbContextFactory<LocalDbContext> factory, SyncEngine engine,
    SyncStatusTracker tracker, IOptions<LocalOptions> options) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var pending = await db.WeighingRecords.CountAsync(r => r.SyncStatus == SyncStatus.Pending, ct);
        var errors = await db.WeighingRecords.CountAsync(r => r.SyncStatus == SyncStatus.Error, ct);
        var st = await db.SyncStates.AsNoTracking().SingleAsync(s => s.Id == 1, ct);
        var snap = tracker.Snapshot();
        var dto = new SyncStatusDto(options.Value.ClientId, snap.Online, snap.Syncing, pending, errors,
            st.LastSuccessfulSyncAt, snap.LastError, snap.NextRetryAt, snap.ConsecutiveFailures);
        return Ok(ApiResponse<SyncStatusDto>.Succeeded(dto));
    }

    [HttpPost("now")]
    public async Task<IActionResult> SyncNow(CancellationToken ct)
    {
        var r = await engine.SyncOnceAsync(ct);
        return Ok(ApiResponse<SyncNowResultDto>.Succeeded(new(r.Outcome.ToString(), r.Error, r.Pushed, r.Pulled)));
    }
}

[ApiController]
[Route("api/local/health")]
public sealed class LocalHealthController : ControllerBase
{
    [HttpGet] public IActionResult Get() => Ok(ApiResponse<object>.Succeeded(new { status = "ok" }));
}
