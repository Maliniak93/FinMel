namespace Skarbiec.MarketData.Sources;

// Registered under Testing:DisableBackgroundJobs, so callers resolve the trigger with no scheduler.
public sealed class NoOpFxHistoryBackfillTrigger : IFxHistoryBackfillTrigger
{
    public Task EnqueueAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
