using Weighing.Server.Api.Data;

namespace Weighing.Server.Api.Services;

public sealed class SyncService(ServerDbContext db, TimeProvider clock, ILogger<SyncService> log)
{
    /// <summary>
    /// Idempotent push. Toàn bộ batch chạy trong 1 transaction và giữ khóa ghi trên SyncCounter từ đầu đến commit:
    ///  - hai push đồng thời bị tuần tự hóa => không race khi kiểm tra trùng Id;
    ///  - sequence được cấp tuần tự và commit đúng thứ tự => mọi reader thấy sequence N thì các sequence < N đã commit.
    /// </summary>
    public async Task<PushResponse> PushAsync(PushRequest req, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // UPDATE "vô hại" để lấy khóa ghi (X lock) trên dòng counter đến hết transaction.
        await db.SyncCounters.Where(c => c.Id == SyncCounter.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Value, c => c.Value), ct);
        var next = await db.SyncCounters.AsNoTracking().Where(c => c.Id == SyncCounter.SingletonId)
            .Select(c => c.Value).SingleAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var ids = req.Records.Select(r => r.Id).Distinct().ToList();
        var existing = await db.WeighingRecords.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var results = new List<PushItemResult>(req.Records.Count);

        foreach (var dto in req.Records)
        {
            var error = dto.ClientId != req.ClientId
                ? "ClientId của bản ghi không khớp ClientId của request."
                : WeighingValidator.Validate(dto, now);
            if (error is not null)
            {
                results.Add(new(dto.Id, PushItemStatus.Invalid, null, error));
                continue;
            }

            if (existing.TryGetValue(dto.Id, out var e))
            {
                if (SameContent(e, dto))
                    results.Add(new(dto.Id, PushItemStatus.Duplicate, e.ServerSequence, null));
                else
                {
                    log.LogWarning("Sync conflict for record {RecordId} from client {ClientId}: same Id, different content", dto.Id, req.ClientId);
                    results.Add(new(dto.Id, PushItemStatus.Conflict, e.ServerSequence, "Id đã tồn tại với nội dung khác."));
                }
                continue;
            }

            var entity = new WeighingRecordEntity
            {
                Id = dto.Id, LicensePlate = dto.LicensePlate, WeightKg = dto.WeightKg, Direction = dto.Direction,
                CargoStatus = dto.CargoStatus, WeighedAt = dto.WeighedAt.ToUniversalTime(), ClientId = dto.ClientId,
                CreatedAtServer = now, ServerSequence = ++next
            };
            db.WeighingRecords.Add(entity);
            existing[dto.Id] = entity;
            results.Add(new(dto.Id, PushItemStatus.Accepted, entity.ServerSequence, null));
        }

        await db.SaveChangesAsync(ct);
        await db.SyncCounters.Where(c => c.Id == SyncCounter.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Value, next), ct);
        await tx.CommitAsync(ct);

        log.LogInformation("Push from {ClientId}: {Accepted} accepted, {Duplicate} duplicate, {Conflict} conflict, {Invalid} invalid",
            req.ClientId, results.Count(r => r.Status == PushItemStatus.Accepted), results.Count(r => r.Status == PushItemStatus.Duplicate),
            results.Count(r => r.Status == PushItemStatus.Conflict), results.Count(r => r.Status == PushItemStatus.Invalid));
        return new PushResponse(results);
    }

    /// <summary>Tra cứu tập trung toàn bộ phiếu của mọi trạm (lọc theo trạm, biển số, khoảng thời gian; phân trang).</summary>
    public async Task<PagedResult<ServerWeighingRecordDto>> QueryRecordsAsync(string? clientId, string? search,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        IQueryable<WeighingRecordEntity> q = db.WeighingRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(clientId)) q = q.Where(r => r.ClientId == clientId);
        var s = PlateNormalizer.Normalize(search);
        if (s.Length > 0) q = q.Where(r => r.LicensePlate.Contains(s));
        if (fromUtc.HasValue) { var f = fromUtc.Value.ToUniversalTime(); q = q.Where(r => r.WeighedAt >= f); }
        if (toUtc.HasValue) { var t = toUtc.Value.ToUniversalTime(); q = q.Where(r => r.WeighedAt < t); }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.WeighedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(r => new ServerWeighingRecordDto(r.Id, r.LicensePlate, r.WeightKg, r.Direction,
            r.CargoStatus, r.WeighedAt, r.ClientId, r.ServerSequence)).ToList(), total, page, pageSize);
    }

    private static bool SameContent(WeighingRecordEntity e, WeighingRecordDto d) =>
        e.LicensePlate == d.LicensePlate && e.WeightKg == d.WeightKg && e.Direction == d.Direction &&
        e.CargoStatus == d.CargoStatus && e.ClientId == d.ClientId &&
        e.WeighedAt.ToUniversalTime().Ticks == d.WeighedAt.ToUniversalTime().Ticks;
}
