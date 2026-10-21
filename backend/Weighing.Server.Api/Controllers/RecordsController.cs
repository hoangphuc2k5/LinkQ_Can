using Microsoft.AspNetCore.Mvc;
using Weighing.Server.Api.Services;

namespace Weighing.Server.Api.Controllers;

/// <summary>Tra cứu tập trung phiếu cân của mọi trạm trên SQL Server (yêu cầu X-Api-Key).</summary>
[ApiController]
[Route("api/records")]
public sealed class RecordsController(SyncService sync) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? clientId, [FromQuery] string? search,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse<PagedResult<ServerWeighingRecordDto>>.Succeeded(
            await sync.QueryRecordsAsync(clientId, search, from, to, page, pageSize, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOne(Guid id, CancellationToken ct)
    {
        var row = await sync.FindAsync(id, ct);
        return row is null
            ? NotFound(ApiResponse<ServerWeighingRecordDto>.Failed("not_found", "Không tìm thấy phiếu cân."))
            : Ok(ApiResponse<ServerWeighingRecordDto>.Succeeded(row));
    }
}
