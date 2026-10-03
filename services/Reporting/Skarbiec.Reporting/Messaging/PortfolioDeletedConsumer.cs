using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

// Valuation history goes too, or GetDashboard would keep a ghost value; cascaded AssetRemoved events never revalue.
public sealed class PortfolioDeletedConsumer(ReportingDbContext db) : IConsumer<PortfolioDeleted>
{
    public async Task Consume(ConsumeContext<PortfolioDeleted> context)
    {
        var portfolioId = context.Message.PortfolioId;
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

        // IgnoreQueryFilters: the snapshots belong to the event's user, not to a request user.
        var snapshots = await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        if (positions.Count == 0 && lines.Count == 0 && snapshots.Count == 0)
        {
            return;
        }

        db.Positions.RemoveRange(positions);
        db.AssetValuations.RemoveRange(lines);
        db.ValuationSnapshots.RemoveRange(snapshots);

        await db.SaveChangesAsync(cancellationToken);
    }
}
