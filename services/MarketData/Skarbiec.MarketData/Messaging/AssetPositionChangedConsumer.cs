using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Messaging;

/// <summary>
/// Keeps MarketData's <see cref="InstrumentUsage"/> read model current from Portfolio's
/// <see cref="AssetPositionChanged"/> (spec-04 design decisions 9, 11-12) — the "in use" set
/// <see cref="PriceSyncJob"/> syncs. The event carries the full position, so nothing here calls
/// Portfolio back (ADR-021).
/// </summary>
/// <remarks>
/// The per-asset <see cref="AssetInstrumentLink"/> is what makes this order-safe: an event whose
/// <c>Version</c> is not newer than the stored link (late or duplicate delivery), or any event for a
/// removed asset, is dropped. Usage is then recounted from the links rather than adjusted by ±1, so an
/// instrument switch, a redelivery and an out-of-order event all converge on the same number.
/// <c>PortfolioIsArchived</c> is deliberately ignored: an archived portfolio's assets keep their
/// prices current for a restore.
/// </remarks>
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
        link.Version = message.Version;

        // Saved first so the recount below sees this link's new state.
        await db.SaveChangesAsync(cancellationToken);

        var newlyUsed = new List<Guid>();
        foreach (var affected in new[] { previousInstrumentId, instrumentId }.OfType<Guid>().Distinct())
        {
            if (await RecountAsync(affected, cancellationToken))
            {
                newlyUsed.Add(affected);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // After SaveChangesAsync: this schedules a Quartz job outside the consume transaction. A
        // duplicate enqueue (a retry after this point) is harmless — backfill upserts by
        // (instrument, date).
        foreach (var affected in newlyUsed)
        {
            await backfillTrigger.EnqueueAsync(affected, cancellationToken);
        }
    }

    /// <summary>Recomputes <see cref="InstrumentUsage.AssetCount"/> from the links and returns whether
    /// the instrument just went from unused to used — the backfill-on-first-use trigger.</summary>
    private async Task<bool> RecountAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var count = await db.AssetInstrumentLinks.CountAsync(
            l => l.InstrumentId == instrumentId && !l.IsRemoved, cancellationToken);
        var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentId, cancellationToken);

        if (usage is null)
        {
            if (count == 0)
            {
                return false;
            }

            // FirstUsedAt is stamped here, once; a later detach-then-reattach reuses this row.
            db.InstrumentUsages.Add(new InstrumentUsage
            {
                InstrumentId = instrumentId,
                AssetCount = count,
                FirstUsedAt = DateTimeOffset.UtcNow,
            });
            return true;
        }

        var wasUnused = usage.AssetCount == 0;
        usage.AssetCount = count;
        return wasUnused && count > 0;
    }
}
