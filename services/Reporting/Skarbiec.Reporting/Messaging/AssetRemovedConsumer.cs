using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Deletes the local <see cref="Position"/> when Portfolio reports the asset gone (spec-03), then
/// revalues today's snapshot of its portfolio from what is left (spec-07, ADR-025).
/// </summary>
/// <remarks>
/// The asset's historical <see cref="AssetValuation"/> lines stay (spec-03 design decision 2):
/// <see cref="Position"/> is current state, a line is already-computed history, and deleting an
/// asset must not silently change what last month's net worth was. A line whose <c>AssetId</c> no
/// longer resolves anywhere is a normal state (ADR-003) — only <c>PortfolioDeleted</c> sweeps lines.
/// The one exception is today's line: <see cref="PortfolioSnapshotWriter.RevalueTodayAsync"/> drops
/// it with the recompute, so today's lines and snapshot agree — a portfolio left empty gets a zero
/// snapshot rather than keeping the pre-removal value (spec-07 AC8). An archived portfolio is left
/// alone, same as on <c>AssetPositionChanged</c>.
/// <para>
/// A removal fanned out by a portfolio delete (<see cref="AssetRemoved.CascadedFromPortfolio"/>,
/// spec-08) only drops the <see cref="Position"/>: <c>PortfolioDeletedConsumer</c> sweeps the lines
/// and snapshots on its own, concurrently consumed queue, so a revaluation here could land after
/// that sweep and write a zero snapshot for a portfolio that no longer exists.
/// </para>
/// </remarks>
public sealed class AssetRemovedConsumer(ReportingDbContext db, PortfolioSnapshotWriter snapshotWriter) : IConsumer<AssetRemoved>
{
    public async Task Consume(ConsumeContext<AssetRemoved> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a consumer has no request user to filter by — see
        // AssetPositionChangedConsumer for the full rationale.
        var position = await db.Positions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.AssetId == message.AssetId, cancellationToken);

        if (position is null)
        {
            return;
        }

        db.Positions.Remove(position);

        // Saved first so the revaluation's read of the portfolio's positions no longer sees this
        // one — still inside the inbox transaction (ADR-012).
        await db.SaveChangesAsync(cancellationToken);

        if (message.CascadedFromPortfolio || position.PortfolioIsArchived)
        {
            return;
        }

        await snapshotWriter.RevalueTodayAsync(position.PortfolioId, message.UserId, cancellationToken);
    }
}
