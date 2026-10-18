using Microsoft.Extensions.Options;
using Weighing.Local.Api.Data;
using Weighing.Local.Api.Sync;

namespace Weighing.Local.Api.Services;

public sealed class WeighingValidationException(string message) : Exception(message);

public sealed class WeighingService(IDbContextFactory<LocalDbContext> factory, IOptions<LocalOptions> options,
    TimeProvider clock, SyncTrigger trigger)
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

    public async Task<PagedResult<LocalWeighingDto>> QueryAsync(string? search, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<LocalWeighingRecord> q = db.WeighingRecords.AsNoTracking();
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
