using MassTransit;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// Flags every <see cref="Position"/> of an archived portfolio so the daily valuation stops valuing
/// it (spec-03), then revalues its snapshot for today, which drops the archived value out of net
/// worth right away (archived-portfolio-out-of-net-worth, ADR-025). Earlier snapshots stay: history
/// before the archive is kept.
/// </summary>
/// <remarks>
/// Belt and braces next to spec-02's per-asset <c>AssetPositionChanged</c> fan-out, which carries
/// the same flag. The order the two arrive in does not matter. If the fan-out lands first, it only
/// flags the positions and this consumer then writes the zero snapshot. If this one lands first, the
/// fan-out finds the flag already set and skips revaluation.
/// </remarks>
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

        // RevalueTodayAsync reads only non-archived positions, so this writes a zero snapshot for
        // today and removes today's lines, exactly as removing the last asset does.
        await snapshotWriter.RevalueTodayAsync(message.PortfolioId, message.UserId, cancellationToken);
    }
}
