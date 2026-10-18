using Microsoft.Extensions.Options;
using Weighing.Local.Api.Data;

namespace Weighing.Local.Api.Sync;

public enum SyncOutcome { Success, Offline, Failed, AlreadyRunning }
public sealed record SyncResult(SyncOutcome Outcome, string? Error = null, int Pushed = 0, int Pulled = 0);

/// <summary>
/// Đồng bộ hai chiều: push (outbox = bản ghi Pending) rồi pull theo cursor.
///  - Không đánh dấu Synced trước khi server xác nhận từng bản ghi.
///  - Pull: ghi bản ghi + cập nhật cursor trong CÙNG một SaveChanges (một transaction).
///  - SemaphoreSlim(1,1) bảo đảm không có hai tiến trình đồng bộ chạy chồng nhau.
/// </summary>
public sealed class SyncEngine(
    IDbContextFactory<LocalDbContext> factory, IServerApiClient server, SyncStatusTracker tracker,
    IOptions<LocalOptions> options, TimeProvider clock, ILogger<SyncEngine> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LocalOptions Opt => options.Value;

    public async Task<SyncResult> SyncOnceAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, CancellationToken.None)) return new(SyncOutcome.AlreadyRunning);
        tracker.SetSyncing(true);
        try
        {
            if (!await server.IsHealthyAsync(ct))
            {
                tracker.SetOffline("Không kết nối được server trung tâm.");
                return new(SyncOutcome.Offline, "Không kết nối được server trung tâm.");
            }
            tracker.SetOnline();
            var pushed = await PushPendingAsync(ct);
            var pulled = await PullAsync(ct);

            await using (var db = await factory.CreateDbContextAsync(CancellationToken.None))
            {
                var st = await db.SyncStates.SingleAsync(s => s.Id == 1, CancellationToken.None);
                st.LastSuccessfulSyncAt = clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            tracker.SetSuccess();
            log.LogInformation("Sync OK for {ClientId}: pushed {Pushed}, pulled {Pulled}", Opt.ClientId, pushed, pulled);
            return new(SyncOutcome.Success, null, pushed, pulled);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Sync failed for {ClientId}: {Reason}", Opt.ClientId, ex.Message);
            if (ex is HttpRequestException or TaskCanceledException) tracker.SetOffline(ex.Message);
            else tracker.SetFailure(ex.Message);
            return new(SyncOutcome.Failed, ex.Message);
        }
        finally
        {
            tracker.SetSyncing(false);
            _gate.Release();
        }
    }

    private async Task<int> PushPendingAsync(CancellationToken ct)
    {
        var synced = 0;
        while (true)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var batch = await db.WeighingRecords.Where(r => r.SyncStatus == SyncStatus.Pending)
                .OrderBy(r => r.WeighedAt).ThenBy(r => r.Id).Take(Opt.PushBatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            PushResponse resp;
            try
            {
                resp = await server.PushAsync(new PushRequest(Opt.ClientId, batch.Select(r => r.ToDto()).ToList()), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Giữ nguyên Pending (không mất dữ liệu), chỉ ghi nhận lần thử thất bại.
                foreach (var r in batch) { r.RetryCount++; r.LastSyncError = Trunc(ex.Message); }
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var byId = resp.Results.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var progressed = 0;
            foreach (var r in batch)
            {
                if (!byId.TryGetValue(r.Id, out var res)) continue;
                if (res.Status is PushItemStatus.Accepted or PushItemStatus.Duplicate)
                {
                    r.SyncStatus = SyncStatus.Synced; r.SyncedAt = now; r.ServerSequence = res.ServerSequence; r.LastSyncError = null;
                    synced++;
                }
                else
                {
                    r.SyncStatus = SyncStatus.Error; r.LastSyncError = Trunc($"{res.Status}: {res.Message}");
                    log.LogError("Record {RecordId} rejected by server: {Status} {Message}", r.Id, res.Status, res.Message);
                }
                progressed++;
            }
            await db.SaveChangesAsync(CancellationToken.None);
            if (progressed == 0) break; // tránh vòng lặp vô hạn nếu server không trả kết quả
        }
        return synced;
    }

    private async Task<int> PullAsync(CancellationToken ct)
    {
        var pulled = 0;
        while (true)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var st = await db.SyncStates.SingleAsync(s => s.Id == 1, ct);
            var page = await server.PullAsync(st.LastServerCursor, Opt.PullPageSize, ct);
            if (page.NextCursor < st.LastServerCursor) throw new InvalidOperationException("Server trả cursor lùi.");

            var now = clock.GetUtcNow().UtcDateTime;
            var ids = page.Records.Select(r => r.Id).ToList();
            var existing = await db.WeighingRecords.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            foreach (var rec in page.Records)
            {
                if (!existing.TryGetValue(rec.Id, out var local))
                {
                    db.WeighingRecords.Add(new LocalWeighingRecord
                    {
                        Id = rec.Id, LicensePlate = rec.LicensePlate, WeightKg = rec.WeightKg, Direction = rec.Direction,
                        CargoStatus = rec.CargoStatus, WeighedAt = rec.WeighedAt.ToUniversalTime(), ClientId = rec.ClientId,
                        SyncStatus = SyncStatus.Synced, SyncedAt = now, ServerSequence = rec.ServerSequence
                    });
                    pulled++;
                }
                else if (SameContent(local, rec))
                {
                    // Server đã có bản ghi của chính mình (vd. phản hồi push bị mất) => xác nhận luôn.
                    if (local.SyncStatus != SyncStatus.Synced) { local.SyncStatus = SyncStatus.Synced; local.SyncedAt = now; local.LastSyncError = null; }
                    local.ServerSequence = rec.ServerSequence;
                }
                else
                {
                    local.SyncStatus = SyncStatus.Error;
                    local.LastSyncError = "Xung đột: cùng Id nhưng nội dung khác với server.";
                    log.LogError("Pull conflict on record {RecordId}", rec.Id);
                }
            }
            st.LastServerCursor = page.NextCursor;
            await db.SaveChangesAsync(CancellationToken.None); // 1 transaction: bản ghi + cursor
            if (!page.HasMore) break;
        }
        return pulled;
    }

    private static bool SameContent(LocalWeighingRecord l, ServerWeighingRecordDto s) =>
        l.LicensePlate == s.LicensePlate && l.WeightKg == s.WeightKg && l.Direction == s.Direction &&
        l.CargoStatus == s.CargoStatus && l.ClientId == s.ClientId &&
        l.WeighedAt.ToUniversalTime().Ticks == s.WeighedAt.ToUniversalTime().Ticks;

    private static string Trunc(string s) => s.Length > 500 ? s[..500] : s;
}
