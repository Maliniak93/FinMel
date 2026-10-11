using MassTransit;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// The per-asset fan-out carries the same flag; whichever lands first, today's snapshot is revalued the same way.
public sealed class PortfolioArchivedConsumer(ReportingDbContext db, SnapshotWriter snapshotWriter)
    : IConsumer<PortfolioArchived>
{
    public async Task Consume(ConsumeContext<PortfolioArchived> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // No positions means nothing was valued: an empty portfolio gets no snapshot, same as restore.
        var (hasPositions, affectedFrom) = await PortfolioArchiveFlag.ApplyAsync(
            db, message.PortfolioId, isArchived: true, message.OccurredAtUtc, cancellationToken);
        if (!hasPositions)
        {
            return;
        }

        if (affectedFrom is { } from && from < snapshotWriter.Today)
        {
            await HistoryRebuildRequests.RequestAsync(db, context, message.UserId, from, cancellationToken);
        }

        // RevalueTodayAsync reads only non-archived positions, so today's snapshot drops this portfolio's lines.
        await snapshotWriter.RevalueTodayAsync(message.UserId, cancellationToken);
    }
}
