using MassTransit;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Mirror of <see cref="PortfolioArchivedConsumer"/>: clears the archived flag on every
/// <see cref="Position"/> of a restored portfolio so the daily valuation picks it up again (spec-03),
/// then revalues its snapshot for today right away instead of waiting for that sync (spec-07 AC9).
/// </summary>
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
