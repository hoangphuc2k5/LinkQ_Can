using Microsoft.Extensions.Options;
using Weighing.Local.Api.Data;

namespace Weighing.Local.Api.Sync;

public enum SyncOutcome { Success, Offline, Failed, AlreadyRunning }
public sealed record SyncResult(SyncOutcome Outcome, string? Error = null, int Pushed = 0, int Pulled = 0);

/// <summary>
/// Đồng bộ một chiều: đẩy (push) các bản ghi Pending của trạm này lên server trung tâm.
///  - Không đánh dấu Synced trước khi server xác nhận từng bản ghi.
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
            var pulled = 0;

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
            // AsNoTracking: chỉ cập nhật cột đồng bộ, không ghi đè cân lần 2 nếu người dùng vừa nhập trong lúc đẩy.
            var batch = await db.WeighingRecords.AsNoTracking().Where(r => r.SyncStatus == SyncStatus.Pending)
                .OrderBy(r => r.WeighedAt).ThenBy(r => r.Id).Take(Opt.PushBatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            PushResponse resp;
            try
            {
                resp = await server.PushAsync(new PushRequest(Opt.ClientId, batch.Select(r => r.ToDto()).ToList()), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var ids = batch.Select(r => r.Id).ToList();
                var message = Trunc(ex.Message);
                await db.WeighingRecords.Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RetryCount, x => x.RetryCount + 1)
                    .SetProperty(x => x.LastSyncError, message), CancellationToken.None);
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
                    await db.WeighingRecords.Where(x => x.Id == r.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.SyncStatus, SyncStatus.Synced)
                        .SetProperty(x => x.SyncedAt, now)
                        .SetProperty(x => x.ServerSequence, res.ServerSequence)
                        .SetProperty(x => x.LastSyncError, (string?)null), CancellationToken.None);
                    synced++;
                }
                else
                {
                    var error = Trunc($"{res.Status}: {res.Message}");
                    await db.WeighingRecords.Where(x => x.Id == r.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.SyncStatus, SyncStatus.Error)
                        .SetProperty(x => x.LastSyncError, error), CancellationToken.None);
                    log.LogError("Record {RecordId} rejected by server: {Status} {Message}", r.Id, res.Status, res.Message);
                }
                progressed++;
            }
            if (progressed == 0) break; // tránh vòng lặp vô hạn nếu server không trả kết quả
        }
        return synced;
    }

    private static string Trunc(string s) => s.Length > 500 ? s[..500] : s;
}
