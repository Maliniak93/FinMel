using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.GetDashboard;

/// <summary>
/// Replaces Portfolio's Phase 1 <c>GetWealthSummary</c> shortcut (T2.12/T2.13, ADR-013/015): the
/// dashboard now reads Reporting's own <see cref="ValuationSnapshot"/> read model instead of
/// re-pricing anything on every visit.
/// </summary>
public sealed class GetDashboardHandler(ReportingDbContext db)
{
    public async Task<DashboardResponse> HandleAsync(Guid? portfolioId, CancellationToken cancellationToken)
    {
        // Each portfolio can be on a different latest date: a per-portfolio compute failure in the
        // DailyPricesSynced consumer (T2.11) leaves that one portfolio's row at whatever date it
        // last succeeded on, while the rest move forward — "latest snapshot per portfolio" is a
        // per-row max, not one global max applied to every portfolio.
        var latestDatePerPortfolio = db.ValuationSnapshots
            .Where(s => portfolioId == null || s.PortfolioId == portfolioId)
            .GroupBy(s => s.PortfolioId)
            .Select(g => new { PortfolioId = g.Key, Date = g.Max(s => s.Date) });

        var snapshots = await db.ValuationSnapshots
            .AsNoTracking()
            .Where(s => portfolioId == null || s.PortfolioId == portfolioId)
            .Join(
                latestDatePerPortfolio,
                s => new { s.PortfolioId, s.Date },
                l => new { l.PortfolioId, l.Date },
                (s, _) => s)
            .ToListAsync(cancellationToken);

        var netWorthPln = snapshots.Sum(s => s.TotalPln);

        var byPortfolio = snapshots
            .Select(s => new DashboardPortfolioValue
            {
                PortfolioId = s.PortfolioId,
                ValuePln = s.TotalPln,
                SnapshotDate = s.Date,
                IsStale = s.IsStale,
            })
            .OrderByDescending(p => p.ValuePln)
            .ToList();

        var byAssetClass = snapshots
            .SelectMany(s => ValuationBreakdown.Deserialize(s.BreakdownJson))
            .GroupBy(e => e.AssetClass)
            .Select(g =>
            {
                var value = g.Sum(e => e.ValuePln);
                return new DashboardAssetClassValue
                {
                    AssetClass = g.Key,
                    ValuePln = value,
                    Percentage = netWorthPln == 0m ? 0m : Math.Round(value / netWorthPln * 100m, 2),
                };
            })
            .OrderByDescending(b => b.ValuePln)
            .ToList();

        return new DashboardResponse
        {
            NetWorthPln = netWorthPln,
            AsOf = snapshots.Count == 0 ? null : snapshots.Max(s => s.Date),
            IsStale = snapshots.Any(s => s.IsStale),
            ByAssetClass = byAssetClass,
            ByPortfolio = byPortfolio,
        };
    }
}
