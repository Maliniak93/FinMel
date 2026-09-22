using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// The one line <see cref="PortfolioArchivedConsumer"/> and <see cref="PortfolioRestoredConsumer"/>
/// share — archive and restore are the same write with a flipped flag.
/// </summary>
internal static class PortfolioArchiveFlag
{
    public static async Task ApplyAsync(
        ReportingDbContext db, Guid portfolioId, bool isArchived, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: a consumer has no request user to filter by — see
        // AssetPositionChangedConsumer for the full rationale. Change tracker + one SaveChanges
        // rather than ExecuteUpdate, so the write shares the transaction that commits the inbox row
        // (ADR-012, spec-03 design decision 5).
        var positions = await db.Positions
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        if (positions.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var position in positions)
        {
            position.PortfolioIsArchived = isArchived;
            position.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
