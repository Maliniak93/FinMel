using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Removes everything Reporting holds for a deleted portfolio: its <see cref="Position"/> rows, its
/// <see cref="AssetValuation"/> lines and its <see cref="ValuationSnapshot"/> rows (spec-03).
/// </summary>
/// <remarks>
/// Valuation history goes too, unlike on <c>AssetRemoved</c> (spec-03 design decision 1):
/// <c>GetDashboard</c> sums the latest snapshot per portfolio, so rows left behind would keep a
/// ghost value in net worth forever. A portfolio delete cascades to its assets (spec-08), so this
/// event arrives alongside one <c>AssetRemoved</c> per asset flagged <c>CascadedFromPortfolio</c>;
/// those only drop their <see cref="Position"/> and never revalue, so whichever queue runs first,
/// this sweep is what removes the portfolio's lines and snapshots — plus any position its
/// <c>AssetRemoved</c> has not removed yet, or never will if it was lost.
/// </remarks>
public sealed class PortfolioDeletedConsumer(ReportingDbContext db) : IConsumer<PortfolioDeleted>
{
    public async Task Consume(ConsumeContext<PortfolioDeleted> context)
    {
        var portfolioId = context.Message.PortfolioId;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a consumer has no request user to filter by — see
        // AssetPositionChangedConsumer for the full rationale. Loaded and removed through the change
        // tracker rather than ExecuteDelete, so all three deletes share the transaction that commits
        // the inbox row (ADR-012, spec-03 design decision 5).
        var positions = await db.Positions
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        var lines = await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

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
