namespace Skarbiec.MarketData.Sources;

// Registered under Testing:DisableBackgroundJobs, so TriggerSync resolves in slice tests with no scheduler.
public sealed class NoOpSyncTrigger : ISyncTrigger
{
    public Task<SyncTriggerOutcome> TriggerAsync(CancellationToken cancellationToken) =>
        Task.FromResult(SyncTriggerOutcome.Started);
}
