using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// HasPositions tells the caller whether there is anything to revalue; AffectedFrom is the earliest archive date this call set or cleared.
internal static class PortfolioArchiveFlag
{
    public static async Task<(bool HasPositions, DateOnly? AffectedFrom)> ApplyAsync(
        ReportingDbContext db,
        Guid portfolioId,
        bool isArchived,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: a consumer has no request user; the change tracker keeps the write in the inbox transaction.
        var positions = await db.Positions
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        if (positions.Count == 0)
        {
            return (false, null);
        }

        var now = DateTimeOffset.UtcNow;
        var archivedOn = HistoryChange.UtcDate(occurredAtUtc);
        DateOnly? affectedFrom = null;

        foreach (var position in positions)
        {
            // Whichever of this event and the asset's own event lands first stamps the date; the other leaves it.
            DateOnly? changedDate;
            if (isArchived)
            {
                changedDate = position.PortfolioArchivedOn is null ? archivedOn : null;
                position.PortfolioArchivedOn ??= archivedOn;
            }
            else
            {
                changedDate = position.PortfolioArchivedOn;
                position.PortfolioArchivedOn = null;
            }

            position.PortfolioIsArchived = isArchived;
            position.UpdatedAt = now;

            if (changedDate is { } date && (affectedFrom is null || date < affectedFrom))
            {
                affectedFrom = date;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, affectedFrom);
    }
}
