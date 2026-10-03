using MassTransit;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// The per-asset fan-out carries the same flag; whichever lands first, the zero snapshot is written once.
public sealed class PortfolioArchivedConsumer(ReportingDbContext db, PortfolioSnapshotWriter snapshotWriter)
    : IConsumer<PortfolioArchived>
{
    public async Task Consume(ConsumeContext<PortfolioArchived> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // No positions means nothing was valued: an empty portfolio gets no snapshot, same as restore.
        if (!await PortfolioArchiveFlag.ApplyAsync(db, message.PortfolioId, isArchived: true, cancellationToken))
        {
            return;
        }

        // RevalueTodayAsync reads only non-archived positions, so this writes a zero snapshot for today.
        await snapshotWriter.RevalueTodayAsync(message.PortfolioId, message.UserId, cancellationToken);
    }
}
