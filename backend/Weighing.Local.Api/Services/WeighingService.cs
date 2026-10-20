using Microsoft.Extensions.Options;
using Weighing.Local.Api.Data;
using Weighing.Local.Api.Sync;

namespace Weighing.Local.Api.Services;

public sealed class WeighingValidationException(string message) : Exception(message);

public sealed class WeighingService(IDbContextFactory<LocalDbContext> factory, IOptions<LocalOptions> options,
    TimeProvider clock, SyncTrigger trigger, IServerApiClient server)
{
    /// <summary>Lưu vào SQLite TRƯỚC (Pending), sau đó mới báo cho Sync Engine. Không phụ thuộc mạng.</summary>
    public async Task<LocalWeighingDto> CreateAsync(CreateWeighingRequest req, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new LocalWeighingRecord
        {
            Id = Guid.NewGuid(),
            LicensePlate = PlateNormalizer.Normalize(req.LicensePlate),
            WeightKg = req.WeightKg, Direction = req.Direction, CargoStatus = req.CargoStatus,
            WeighedAt = now, ClientId = options.Value.ClientId, SyncStatus = SyncStatus.Pending
        };
        var error = WeighingValidator.Validate(entity.ToDto(), now);
        if (error is not null) throw new WeighingValidationException(error);

        await using var db = await factory.CreateDbContextAsync(ct);
        db.WeighingRecords.Add(entity);
        await db.SaveChangesAsync(ct);
        trigger.Signal();
        return entity.ToLocalDto();
    }

    /// <summary>
    /// Lịch sử: ưu tiên server (mọi trạm). Nếu server không trả lời thì đọc SQLite cục bộ (chỉ phiếu của trạm này).
    /// Phiếu chưa lên server (Pending/Error) luôn lấy từ SQLite và được ghim lên đầu trang 1.
    /// </summary>
    public async Task<HistoryResult> QueryAsync(string? search, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.HistoryTimeoutSeconds)));
            var remote = await server.QueryRecordsAsync(null, search, fromUtc, toUtc, page, pageSize, timeout.Token);
            var pending = await PendingLocalAsync(search, fromUtc, toUtc, ct);
            var remoteIds = remote.Items.Select(x => x.Id).ToHashSet();
            // Ghim phiếu chưa lên server lên đầu trang 1; loại phiếu trùng Id để không hiện hai lần.
            var pinned = page == 1 ? pending.Where(p => !remoteIds.Contains(p.Id)).ToList() : [];
            var items = pinned.Select(p => p.ToLocalDto())
                .Concat(remote.Items.Select(x => new LocalWeighingDto(x.Id, x.LicensePlate, x.WeightKg, x.Direction,
                    x.CargoStatus, x.WeighedAt, x.ClientId, SyncStatus.Synced, x.WeighedAt, null, 0)))
                .ToList();
            var total = remote.Total + pinned.Count;
            return new HistoryResult(items, total, page, pageSize, "server", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ServerApiException
                                       && !ct.IsCancellationRequested)
        {
            var local = await LocalOnlyAsync(search, fromUtc, toUtc, page, pageSize, ct);
            return new HistoryResult(local.Items, local.Total, local.Page, local.PageSize, "local",
                "Server không khả dụng: chỉ hiển thị phiếu cân đã lưu trên máy này.");
        }
    }

    private async Task<List<LocalWeighingRecord>> PendingLocalAsync(string? search, DateTime? fromUtc, DateTime? toUtc,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<LocalWeighingRecord> q = db.WeighingRecords.AsNoTracking()
            .Where(r => r.ClientId == options.Value.ClientId && r.SyncStatus != SyncStatus.Synced);
        var s = PlateNormalizer.Normalize(search);
        if (s.Length > 0) q = q.Where(r => r.LicensePlate.Contains(s));
        if (fromUtc.HasValue) { var f = fromUtc.Value.ToUniversalTime(); q = q.Where(r => r.WeighedAt >= f); }
        if (toUtc.HasValue) { var t = toUtc.Value.ToUniversalTime(); q = q.Where(r => r.WeighedAt < t); }
        return await q.OrderByDescending(r => r.WeighedAt).ThenBy(r => r.Id).ToListAsync(ct);
    }

    private async Task<PagedResult<LocalWeighingDto>> LocalOnlyAsync(string? search, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Offline: chỉ có phiếu do chính trạm này cân (dữ liệu các trạm khác không được sao chép về).
        IQueryable<LocalWeighingRecord> q = db.WeighingRecords.AsNoTracking()
            .Where(r => r.ClientId == options.Value.ClientId);
        var s = PlateNormalizer.Normalize(search);
        if (s.Length > 0) q = q.Where(r => r.LicensePlate.Contains(s));
        if (fromUtc.HasValue) { var f = fromUtc.Value.ToUniversalTime(); q = q.Where(r => r.WeighedAt >= f); }
        if (toUtc.HasValue) { var t = toUtc.Value.ToUniversalTime(); q = q.Where(r => r.WeighedAt < t); }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.WeighedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(r => r.ToLocalDto()).ToList(), total, page, pageSize);
    }
}
