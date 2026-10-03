using MassTransit;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

public sealed class PortfolioRestoredConsumer(ReportingDbContext db, PortfolioSnapshotWriter snapshotWriter)
    : IConsumer<PortfolioRestored>
{
    public async Task Consume(ConsumeContext<PortfolioRestored> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // No positions means nothing to value: an empty portfolio gets no snapshot, same as the sync.
        if (!await PortfolioArchiveFlag.ApplyAsync(db, message.PortfolioId, isArchived: false, cancellationToken))
        {
            return;
        }

        await snapshotWriter.RevalueTodayAsync(message.PortfolioId, message.UserId, cancellationToken);
    }
}
