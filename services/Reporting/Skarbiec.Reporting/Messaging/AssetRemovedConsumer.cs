using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Deletes the local <see cref="Position"/> when Portfolio reports the asset gone (spec-03).
/// </summary>
/// <remarks>
/// The asset's historical <see cref="AssetValuation"/> lines stay (spec-03 design decision 2):
/// <see cref="Position"/> is current state, a line is already-computed history, and deleting an
/// asset must not silently change what last month's net worth was. A line whose <c>AssetId</c> no
/// longer resolves anywhere is a normal state (ADR-003) — only <c>PortfolioDeleted</c> sweeps lines.
/// </remarks>
public sealed class AssetRemovedConsumer(ReportingDbContext db) : IConsumer<AssetRemoved>
{
    public async Task Consume(ConsumeContext<AssetRemoved> context)
    {
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a consumer has no request user to filter by — see
        // AssetPositionChangedConsumer for the full rationale.
        var position = await db.Positions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.AssetId == context.Message.AssetId, cancellationToken);

        if (position is null)
        {
            return;
        }

        db.Positions.Remove(position);
        await db.SaveChangesAsync(cancellationToken);
    }
}
