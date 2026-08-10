namespace Skarbiec.MarketData.Sources;

/// <summary>
/// <see cref="ISyncTrigger"/> registered instead of <see cref="QuartzSyncTrigger"/> when
/// <c>Testing:DisableBackgroundJobs</c> is set (see <see cref="PriceSyncJobExtensions"/>) — mirrors
/// <see cref="NoOpHistoryBackfillTrigger"/>. Features/TriggerSync (T2.14) still needs the interface
/// resolvable under <c>SkarbiecApiFactory</c>-based HTTP slice tests even though there's no live
/// Quartz scheduler there to enqueue onto.
/// </summary>
public sealed class NoOpSyncTrigger : ISyncTrigger
{
    public Task<SyncTriggerOutcome> TriggerAsync(CancellationToken cancellationToken) =>
        Task.FromResult(SyncTriggerOutcome.Started);
}
