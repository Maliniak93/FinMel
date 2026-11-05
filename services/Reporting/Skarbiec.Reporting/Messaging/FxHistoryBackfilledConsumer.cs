using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

public sealed class FxHistoryBackfilledConsumer(ReportingDbContext db) : IConsumer<FxHistoryBackfilled>
{
    public async Task Consume(ConsumeContext<FxHistoryBackfilled> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: an FX backfill concerns every user holding a position.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        foreach (var group in positions.GroupBy(p => p.UserId))
        {
            var firstDates = group
                .Where(p => p.QuantityHistory.Count > 0)
                .Select(p => p.QuantityHistory.Min(point => point.Date))
                .ToList();
            if (firstDates.Count == 0)
            {
                continue;
            }

            var from = firstDates.Min();
            if (from > message.To)
            {
                continue;
            }

            if (from < message.From)
            {
                from = message.From;
            }

            await HistoryRebuildRequests.RequestAsync(db, context, group.Key, from, cancellationToken);
        }
    }
}
