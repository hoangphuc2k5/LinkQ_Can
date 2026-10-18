using Microsoft.Extensions.Options;

namespace Weighing.Local.Api.Sync;

public sealed class SyncBackgroundService(SyncEngine engine, SyncStatusTracker tracker, SyncTrigger trigger,
    IOptions<LocalOptions> options, TimeProvider clock, ILogger<SyncBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Yield();
        var o = options.Value;
        while (!stop.IsCancellationRequested)
        {
            try { await engine.SyncOnceAsync(stop); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Unexpected sync loop error"); }

            var failures = tracker.ConsecutiveFailures;
            var delay = failures > 0
                ? Backoff.Compute(failures, o.BackoffBaseSeconds, o.BackoffMaxSeconds)
                : TimeSpan.FromSeconds(o.SyncIntervalSeconds);
            tracker.SetNextRetry(clock.GetUtcNow().UtcDateTime + delay);
            try { await trigger.WaitAsync(delay, stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
