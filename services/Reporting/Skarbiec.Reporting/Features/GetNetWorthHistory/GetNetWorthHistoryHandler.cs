using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

/// <summary>
/// Backs the net-worth history chart (E5, T2.13: 1M/1Y/YTD/MAX switcher). Sums
/// <see cref="ValuationSnapshot.TotalPln"/> across the user's portfolios per date, in SQL — the
/// (UserId, Date) index (T2.12) keeps this an index range scan even on a seeded year of daily rows.
/// </summary>
public sealed class GetNetWorthHistoryHandler(ReportingDbContext db, TimeProvider timeProvider)
{
    public async Task<Result<NetWorthHistoryResponse>> HandleAsync(
        string range, Guid? portfolioId, CancellationToken cancellationToken)
    {
        var startResult = ResolveRangeStart(range);
        if (startResult.IsFailure)
        {
            return startResult.Error;
        }

        var start = startResult.Value;

        var points = await db.ValuationSnapshots
            .AsNoTracking()
            .Where(s => portfolioId == null || s.PortfolioId == portfolioId)
            .Where(s => start == null || s.Date >= start)
            .GroupBy(s => s.Date)
            .Select(g => new { Date = g.Key, NetWorthPln = g.Sum(s => s.TotalPln) })
            .OrderBy(p => p.Date)
            .ToListAsync(cancellationToken);

        return new NetWorthHistoryResponse
        {
            Range = range.ToUpperInvariant(),
            Points = points
                .Select(p => new NetWorthHistoryPoint { Date = p.Date, NetWorthPln = p.NetWorthPln })
                .ToList(),
        };
    }

    /// <summary>
    /// MAX has no lower bound (the earliest snapshot naturally becomes the first point) — everything
    /// else is resolved against "today" (<see cref="timeProvider"/>), not the latest snapshot date,
    /// so a stale sync doesn't silently shrink the requested window.
    /// </summary>
    private Result<DateOnly?> ResolveRangeStart(string range)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DateOnly? start;

        switch (range.ToUpperInvariant())
        {
            case "1M":
                start = today.AddMonths(-1);
                break;
            case "1Y":
                start = today.AddYears(-1);
                break;
            case "YTD":
                start = new DateOnly(today.Year, 1, 1);
                break;
            case "MAX":
                start = null;
                break;
            default:
                return NetWorthHistoryErrors.InvalidRange(range);
        }

        return start;
    }
}
