using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Messaging;

// Guarded by a tombstone: the row stays, so a redelivered removal and a late add are no-ops.
public sealed class WatchlistItemRemovedConsumer(MarketDataDbContext db) : IConsumer<WatchlistItemRemoved>
{
    public async Task Consume(ConsumeContext<WatchlistItemRemoved> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        var link = await db.InstrumentWatchLinks.SingleOrDefaultAsync(
            l => l.WatchlistItemId == message.WatchlistItemId, cancellationToken);

        if (link is { IsRemoved: true })
        {
            return;
        }

        if (link is null)
        {
            link = new InstrumentWatchLink { WatchlistItemId = message.WatchlistItemId, InstrumentId = message.InstrumentId };
            db.InstrumentWatchLinks.Add(link);
        }

        link.IsRemoved = true;

        // Saved first so the recount below skips this link.
        await db.SaveChangesAsync(cancellationToken);

        var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == message.InstrumentId, cancellationToken);
        if (usage is not null)
        {
            usage.WatchCount = await db.InstrumentWatchLinks.CountAsync(
                l => l.InstrumentId == message.InstrumentId && !l.IsRemoved, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
