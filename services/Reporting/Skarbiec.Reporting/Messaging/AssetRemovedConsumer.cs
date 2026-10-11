using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

public sealed class AssetRemovedConsumer(ReportingDbContext db, SnapshotWriter snapshotWriter) : IConsumer<AssetRemoved>
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

        // IgnoreQueryFilters: a consumer has no request user.
        var earliestLine = await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.AssetId == message.AssetId)
            .MinAsync(l => (DateOnly?)l.Date, cancellationToken);

        if (earliestLine is { } from && from < snapshotWriter.Today)
        {
            await HistoryRebuildRequests.RequestAsync(db, context, message.UserId, from, cancellationToken);
        }

        db.Positions.Remove(position);

        // Saved first so the revaluation skips this position, inside the same inbox transaction.
        await db.SaveChangesAsync(cancellationToken);

        await snapshotWriter.RevalueTodayAsync(message.UserId, cancellationToken);
    }
}
