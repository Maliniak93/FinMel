using MassTransit;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Mirror of <see cref="PortfolioArchivedConsumer"/>: clears the archived flag on every
/// <see cref="Position"/> of a restored portfolio so the daily valuation picks it up again (spec-03).
/// </summary>
public sealed class PortfolioRestoredConsumer(ReportingDbContext db) : IConsumer<PortfolioRestored>
{
    public Task Consume(ConsumeContext<PortfolioRestored> context) =>
        PortfolioArchiveFlag.ApplyAsync(db, context.Message.PortfolioId, isArchived: false, context.CancellationToken);
}
