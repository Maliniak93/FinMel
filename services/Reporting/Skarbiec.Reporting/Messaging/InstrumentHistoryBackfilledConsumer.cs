using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

public sealed class InstrumentHistoryBackfilledConsumer(ReportingDbContext db, PortfolioSnapshotWriter snapshotWriter)
    : IConsumer<InstrumentHistoryBackfilled>
{
    public async Task Consume(ConsumeContext<InstrumentHistoryBackfilled> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a backfill concerns every user holding the instrument.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.InstrumentId == message.InstrumentId)
            .ToListAsync(cancellationToken);

        foreach (var group in positions.GroupBy(p => p.PortfolioId))
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
            if (from < message.From)
            {
                from = message.From;
            }

            if (from > message.To || from >= snapshotWriter.Today)
            {
                continue;
            }

            await HistoryRebuildRequests.RequestAsync(db, context, group.Key, group.First().UserId, from, cancellationToken);
        }
    }
}
