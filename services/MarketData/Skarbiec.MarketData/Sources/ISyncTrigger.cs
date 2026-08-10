namespace Skarbiec.MarketData.Sources;

/// <summary>Outcome of an on-demand <see cref="PriceSyncJob"/> trigger (T2.14).</summary>
public enum SyncTriggerOutcome
{
    Started,
    AlreadyRunning,
}

/// <summary>
/// Fires <see cref="PriceSyncJob"/> now instead of waiting for its cron schedule (T2.14, E4 [S]) —
/// for demos, debugging and impatience. Distinct from <see cref="IHistoryBackfillTrigger"/>: that one
/// enqueues a per-instrument one-off job, this one runs the whole daily sync on demand and must
/// refuse a second call while the first is still in flight (double-click AC).
/// </summary>
public interface ISyncTrigger
{
    Task<SyncTriggerOutcome> TriggerAsync(CancellationToken cancellationToken);
}
