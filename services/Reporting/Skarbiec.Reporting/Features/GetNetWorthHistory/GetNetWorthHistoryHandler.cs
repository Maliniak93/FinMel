using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.GetNetWorthHistory;

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

        decimal? changePln = null;
        decimal? changePercent = null;
        if (points.Count >= 2)
        {
            var first = points[0].NetWorthPln;
            changePln = points[^1].NetWorthPln - first;
            changePercent = first == 0m ? null : Math.Round(changePln.Value / first * 100m, 2);
        }

        return new NetWorthHistoryResponse
        {
            Range = range.ToUpperInvariant(),
            Points = points
                .Select(p => new NetWorthHistoryPoint { Date = p.Date, NetWorthPln = p.NetWorthPln })
                .ToList(),
            ChangePln = changePln,
            ChangePercent = changePercent,
        };
    }

    // Resolved against today, not the latest snapshot, so a stale sync never shrinks the window; MAX has no lower bound.
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
