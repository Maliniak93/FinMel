using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Messaging;

// Guarded by a tombstone, not a version: the row stays, so a redelivered removal and any later position event are no-ops.
public sealed class AssetRemovedConsumer(MarketDataDbContext db) : IConsumer<AssetRemoved>
{
    public async Task Consume(ConsumeContext<AssetRemoved> context)
    {
        var cancellationToken = context.CancellationToken;
        var assetId = context.Message.AssetId;

        var link = await db.AssetInstrumentLinks.SingleOrDefaultAsync(l => l.AssetId == assetId, cancellationToken);

        if (link is { IsRemoved: true })
        {
            return;
        }

        if (link is null)
        {
            link = new AssetInstrumentLink { AssetId = assetId };
            db.AssetInstrumentLinks.Add(link);
        }

        var previousInstrumentId = link.InstrumentId;
        link.IsRemoved = true;
        link.InstrumentId = null;

        // Saved first so the recount below skips this link.
        await db.SaveChangesAsync(cancellationToken);

        if (previousInstrumentId is not { } instrumentId)
        {
            return;
        }

        // A removal only lowers a count, so there is no usage row to create or backfill to enqueue.
        var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentId, cancellationToken);
        if (usage is not null)
        {
            usage.AssetCount = await db.AssetInstrumentLinks.CountAsync(
                l => l.InstrumentId == instrumentId && !l.IsRemoved, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
