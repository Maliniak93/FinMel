using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Messaging;

/// <summary>
/// Releases a deleted asset's hold on its instrument (spec-04 design decision 10): the
/// <see cref="AssetInstrumentLink"/> becomes a terminal tombstone and the old instrument's
/// <see cref="InstrumentUsage"/> is recounted from the remaining links.
/// </summary>
/// <remarks>
/// <see cref="AssetRemoved"/> carries no version, which is why the tombstone — not a version compare —
/// guards it: the row stays, so a redelivered <see cref="AssetRemoved"/> and any later
/// <see cref="AssetPositionChanged"/> for the asset are both no-ops. A removal arriving before the
/// asset's first position event still writes the tombstone, so that late event is dropped too.
/// </remarks>
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

        // Saved first so the recount below no longer sees this link.
        await db.SaveChangesAsync(cancellationToken);

        if (previousInstrumentId is not { } instrumentId)
        {
            return;
        }

        // A removal can only lower a count, so there is never a usage row to create or a backfill to
        // enqueue here.
        var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentId, cancellationToken);
        if (usage is not null)
        {
            usage.AssetCount = await db.AssetInstrumentLinks.CountAsync(
                l => l.InstrumentId == instrumentId && !l.IsRemoved, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
