using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// The portfolio's lines go too, then the rebuild rewrites the user's snapshots without it; cascaded AssetRemoved events never revalue.
public sealed class PortfolioDeletedConsumer(ReportingDbContext db, SnapshotWriter snapshotWriter) : IConsumer<PortfolioDeleted>
{
    public async Task Consume(ConsumeContext<PortfolioDeleted> context)
    {
        var message = context.Message;
        var portfolioId = message.PortfolioId;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a consumer has no request user; the change tracker keeps the deletes in the inbox transaction.
        var positions = await db.Positions
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        // IgnoreQueryFilters: the lines belong to the event's user, not to a request user.
        var lines = await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        if (positions.Count == 0 && lines.Count == 0)
        {
            return;
        }

        var earliestLine = lines.Count == 0 ? (DateOnly?)null : lines.Min(l => l.Date);

        db.Positions.RemoveRange(positions);
        db.AssetValuations.RemoveRange(lines);

        if (earliestLine is { } from && from < snapshotWriter.Today)
        {
            await HistoryRebuildRequests.RequestAsync(db, context, message.UserId, from, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        await snapshotWriter.RevalueTodayAsync(message.UserId, cancellationToken);
    }
}
