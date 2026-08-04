namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Enqueues a one-off <see cref="HistoryBackfillJob"/> run for an instrument without ever calling an
/// external price API inline — ADR-007's spirit ("no external calls in the request path") applies to
/// backfill the same way it applies to the daily sync (T2.6). A caller (T2.8's custom-instrument add,
/// T2.9's attach-to-asset) only needs to know an instrument just entered use; the actual fetch happens
/// later, off Quartz's own thread pool, well after this returns.
/// </summary>
public interface IHistoryBackfillTrigger
{
    Task EnqueueAsync(Guid instrumentId, CancellationToken cancellationToken);
}
