using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

// Queues whatever history is behind what live holdings need: a backfill that ended in error or a link older than coverage.
public sealed class HistoryCoverageReconciler(
    MarketDataDbContext db,
    IHistoryBackfillTrigger instrumentTrigger,
    IFxHistoryBackfillTrigger fxTrigger,
    TimeProvider timeProvider,
    ILogger<HistoryCoverageReconciler> logger,
    FxHistoryOptions? fxHistoryOptions = null)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (await FxHistoryNeed.AnyLaggingAsync(db, today, fxHistoryOptions, cancellationToken))
        {
            await fxTrigger.EnqueueAsync(cancellationToken);
            logger.LogInformation("Coverage reconcile: FX history is behind; backfill enqueued.");
        }

        var required = await db.AssetInstrumentLinks
            .Where(l => !l.IsRemoved && l.InstrumentId != null && l.FirstTransactionDate != null)
            .GroupBy(l => l.InstrumentId!.Value)
            .Select(g => new { InstrumentId = g.Key, From = g.Min(l => l.FirstTransactionDate) })
            .ToListAsync(cancellationToken);
        var ids = required.Select(r => r.InstrumentId).ToList();
        var covered = await db.Instruments
            .AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.HistoryCoveredFrom, cancellationToken);

        foreach (var (instrumentId, from) in required.Select(r => (r.InstrumentId, From: r.From!.Value)))
        {
            if (covered.TryGetValue(instrumentId, out var coveredFrom) && coveredFrom is { } date && date <= from)
            {
                continue;
            }

            await instrumentTrigger.EnqueueAsync(instrumentId, from, cancellationToken);
            logger.LogInformation(
                "Coverage reconcile: instrument {InstrumentId} history is behind; backfill from {From} enqueued.", instrumentId, from);
        }
    }
}
