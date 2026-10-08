using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Messaging;

// Order-safe: an event not newer than its link, or for a removed asset, is dropped, and usage is recounted from the links.
public sealed class AssetPositionChangedConsumer(
    MarketDataDbContext db,
    IHistoryBackfillTrigger backfillTrigger,
    ILogger<AssetPositionChangedConsumer> logger) : IConsumer<AssetPositionChanged>
{
    public async Task Consume(ConsumeContext<AssetPositionChanged> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        var link = await db.AssetInstrumentLinks.SingleOrDefaultAsync(l => l.AssetId == message.AssetId, cancellationToken);

        if (link is not null && (link.IsRemoved || link.Version >= message.Version))
        {
            logger.LogInformation(
                "AssetPositionChangedConsumer: ignoring event for asset {AssetId} (version {EventVersion}; stored {StoredVersion}, removed {IsRemoved}).",
                message.AssetId, message.Version, link.Version, link.IsRemoved);
            return;
        }

        var previousInstrumentId = link?.InstrumentId;
        var instrumentId = message.ValuationMode is AssetValuationMode.Market ? message.InstrumentId : null;

        if (link is null)
        {
            link = new AssetInstrumentLink { AssetId = message.AssetId };
            db.AssetInstrumentLinks.Add(link);
        }

        link.InstrumentId = instrumentId;
        link.FirstTransactionDate = message.FirstTransactionDate;
        link.Version = message.Version;

        // Saved first so the recount below sees this link's new state.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var affected in new[] { previousInstrumentId, instrumentId }.OfType<Guid>().Distinct())
        {
            await RecountAsync(affected, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (instrumentId is { } heldInstrumentId)
        {
            await EnqueueMissingHistoryAsync(heldInstrumentId, cancellationToken);
        }
    }

    // The history window only grows backwards: a backfill is due when a live holding's first transaction predates what is covered.
    private async Task EnqueueMissingHistoryAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var required = await db.AssetInstrumentLinks
            .Where(l => l.InstrumentId == instrumentId && !l.IsRemoved)
            .MinAsync(l => l.FirstTransactionDate, cancellationToken);
        if (required is null)
        {
            return;
        }

        var coveredFrom = await db.Instruments
            .Where(i => i.Id == instrumentId)
            .Select(i => i.HistoryCoveredFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (coveredFrom is not null && coveredFrom <= required)
        {
            return;
        }

        // Scheduled after the save, outside the consume transaction; a duplicate enqueue is harmless, as backfill upserts.
        await backfillTrigger.EnqueueAsync(instrumentId, required.Value, cancellationToken);
    }

    private async Task RecountAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var count = await db.AssetInstrumentLinks.CountAsync(
            l => l.InstrumentId == instrumentId && !l.IsRemoved, cancellationToken);
        var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentId, cancellationToken);

        if (usage is null)
        {
            if (count == 0)
            {
                return;
            }

            // FirstUsedAt is stamped here, once; a later detach-then-reattach reuses this row.
            db.InstrumentUsages.Add(new InstrumentUsage
            {
                InstrumentId = instrumentId,
                AssetCount = count,
                FirstUsedAt = DateTimeOffset.UtcNow,
            });
            return;
        }

        usage.AssetCount = count;
    }
}
