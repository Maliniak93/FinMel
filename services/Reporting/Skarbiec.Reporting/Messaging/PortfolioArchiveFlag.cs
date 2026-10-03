using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Messaging;

// Returns whether the portfolio has any positions, so the caller knows whether there is anything to revalue.
internal static class PortfolioArchiveFlag
{
    public static async Task<bool> ApplyAsync(
        ReportingDbContext db, Guid portfolioId, bool isArchived, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: a consumer has no request user; the change tracker keeps the write in the inbox transaction.
        var positions = await db.Positions
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        if (positions.Count == 0)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var position in positions)
        {
            position.PortfolioIsArchived = isArchived;
            position.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
