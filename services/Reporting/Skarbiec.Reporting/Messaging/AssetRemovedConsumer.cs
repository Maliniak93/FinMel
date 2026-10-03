using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// History stays for PortfolioDeleted to sweep, and a cascaded removal skips revaluation so it never races that sweep.
public sealed class AssetRemovedConsumer(ReportingDbContext db, PortfolioSnapshotWriter snapshotWriter) : IConsumer<AssetRemoved>
{
    public async Task Consume(ConsumeContext<AssetRemoved> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: a consumer has no request user and writes for the user the event names.
        var position = await db.Positions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.AssetId == message.AssetId, cancellationToken);

        if (position is null)
        {
            return;
        }

        db.Positions.Remove(position);

        // Saved first so the revaluation skips this position, inside the same inbox transaction.
        await db.SaveChangesAsync(cancellationToken);

        if (message.CascadedFromPortfolio || position.PortfolioIsArchived)
        {
            return;
        }

        await snapshotWriter.RevalueTodayAsync(position.PortfolioId, message.UserId, cancellationToken);
    }
}
