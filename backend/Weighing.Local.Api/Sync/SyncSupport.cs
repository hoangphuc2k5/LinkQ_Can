namespace Weighing.Local.Api.Sync;

public static class Backoff
{
    /// <summary>Exponential backoff có giới hạn: base * 2^(failures-1), tối đa max.</summary>
    public static TimeSpan Compute(int failures, int baseSeconds, int maxSeconds)
    {
        if (failures <= 0) return TimeSpan.Zero;
        var exp = Math.Min(failures - 1, 20);
        return TimeSpan.FromSeconds(Math.Min(baseSeconds * Math.Pow(2, exp), maxSeconds));
    }
}

public sealed class SyncTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    public void Signal() { try { _signal.Release(); } catch (SemaphoreFullException) { } }
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct) => await _signal.WaitAsync(timeout, ct);
}

public sealed record TrackerSnapshot(bool? Online, bool Syncing, string? LastError, DateTime? NextRetryAt, int ConsecutiveFailures);

public sealed class SyncStatusTracker
{
    private readonly object _lock = new();
    private bool? _online;
    private bool _syncing;
    private string? _lastError;
    private DateTime? _nextRetry;
    private int _failures;

    public void SetSyncing(bool v) { lock (_lock) _syncing = v; }
    public void SetOnline() { lock (_lock) _online = true; }
    public void SetOffline(string reason) { lock (_lock) { _online = false; _lastError = reason; _failures++; } }
    public void SetFailure(string error) { lock (_lock) { _lastError = error; _failures++; } }
    public void SetSuccess() { lock (_lock) { _online = true; _lastError = null; _failures = 0; _nextRetry = null; } }
    public void SetNextRetry(DateTime? at) { lock (_lock) _nextRetry = at; }
    public int ConsecutiveFailures { get { lock (_lock) return _failures; } }
    public TrackerSnapshot Snapshot() { lock (_lock) return new(_online, _syncing, _lastError, _nextRetry, _failures); }
}
