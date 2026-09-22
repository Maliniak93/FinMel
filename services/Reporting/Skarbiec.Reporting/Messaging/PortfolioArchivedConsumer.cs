using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Flags every <see cref="Position"/> of an archived portfolio so the daily valuation stops valuing
/// it (spec-03). Belt and braces next to spec-02's per-asset <c>AssetPositionChanged</c> fan-out,
/// which carries the same flag: either message alone leaves the read model correct.
/// </summary>
public sealed class PortfolioArchivedConsumer(ReportingDbContext db) : IConsumer<PortfolioArchived>
{
    public Task Consume(ConsumeContext<PortfolioArchived> context) =>
        PortfolioArchiveFlag.ApplyAsync(db, context.Message.PortfolioId, isArchived: true, context.CancellationToken);
}
