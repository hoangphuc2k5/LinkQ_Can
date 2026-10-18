using Weighing.Server.Api.Data;

namespace Weighing.Server.Api.Services;

public sealed class SyncService(ServerDbContext db, TimeProvider clock, ILogger<SyncService> log)
{
    /// <summary>
    /// Idempotent push. Toàn bộ batch chạy trong 1 transaction và giữ khóa ghi trên SyncCounter từ đầu đến commit:
    ///  - hai push đồng thời bị tuần tự hóa => không race khi kiểm tra trùng Id;
    ///  - sequence được cấp tuần tự và commit đúng thứ tự => pull theo cursor không bao giờ bỏ sót bản ghi.
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

    public async Task<PullResponse> PullAsync(long cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var rows = await db.WeighingRecords.AsNoTracking()
            .Where(r => r.ServerSequence > cursor).OrderBy(r => r.ServerSequence).Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var nextCursor = rows.Count > 0 ? rows[^1].ServerSequence : cursor;
        return new PullResponse(rows.Select(r => new ServerWeighingRecordDto(r.Id, r.LicensePlate, r.WeightKg, r.Direction,
            r.CargoStatus, r.WeighedAt, r.ClientId, r.ServerSequence)).ToList(), nextCursor, hasMore);
    }

    private static bool SameContent(WeighingRecordEntity e, WeighingRecordDto d) =>
        e.LicensePlate == d.LicensePlate && e.WeightKg == d.WeightKg && e.Direction == d.Direction &&
        e.CargoStatus == d.CargoStatus && e.ClientId == d.ClientId &&
        e.WeighedAt.ToUniversalTime().Ticks == d.WeighedAt.ToUniversalTime().Ticks;
}
