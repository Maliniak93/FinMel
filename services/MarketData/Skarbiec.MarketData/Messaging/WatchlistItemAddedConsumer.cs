using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Messaging;

public sealed class WatchlistItemAddedConsumer(
    MarketDataDbContext db,
    IHistoryBackfillTrigger backfillTrigger) : IConsumer<WatchlistItemAdded>
{
    private const int RecentWindowDays = 14;

    public async Task Consume(ConsumeContext<WatchlistItemAdded> context)
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
            await db.SaveChangesAsync(cancellationToken);
        }

        var count = await db.InstrumentWatchLinks.CountAsync(
            l => l.InstrumentId == message.InstrumentId && !l.IsRemoved, cancellationToken);
        var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == message.InstrumentId, cancellationToken);

        if (usage is null)
        {
            db.InstrumentUsages.Add(new InstrumentUsage
            {
                InstrumentId = message.InstrumentId,
                WatchCount = count,
                FirstUsedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            usage.WatchCount = count;
        }

        await db.SaveChangesAsync(cancellationToken);

        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-RecentWindowDays);
        var coveredFrom = await db.Instruments
            .Where(i => i.Id == message.InstrumentId)
            .Select(i => i.HistoryCoveredFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (coveredFrom is not null && coveredFrom <= from)
        {
            return;
        }

        // Scheduled after the save, outside the consume transaction; a duplicate enqueue is harmless, as backfill upserts.
        await backfillTrigger.EnqueueAsync(message.InstrumentId, from, cancellationToken);
    }
}
