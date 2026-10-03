namespace Skarbiec.MarketData.Sources;

// Registered under Testing:DisableBackgroundJobs, so request-path callers resolve the trigger with no scheduler.
public sealed class NoOpHistoryBackfillTrigger : IHistoryBackfillTrigger
{
    public Task EnqueueAsync(Guid instrumentId, CancellationToken cancellationToken) => Task.CompletedTask;
}
