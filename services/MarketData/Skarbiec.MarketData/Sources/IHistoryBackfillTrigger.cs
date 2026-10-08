namespace Skarbiec.MarketData.Sources;

// Only schedules: the external fetch runs later on Quartz's thread pool, never in the request path.
public interface IHistoryBackfillTrigger
{
    Task EnqueueAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken);
}
