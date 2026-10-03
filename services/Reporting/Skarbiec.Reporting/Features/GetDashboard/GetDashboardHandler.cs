using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.GetDashboard;

public sealed class GetDashboardHandler(ReportingDbContext db)
{
    public async Task<DashboardResponse> HandleAsync(Guid? portfolioId, CancellationToken cancellationToken)
    {
        // A per-portfolio compute failure leaves that portfolio at its last good date, so "latest" is a per-row max.
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

        // Lines are tenancy-filtered in their own right, so this never reaches another user's assets.
        var assetClassTotals = await db.AssetValuations
            .AsNoTracking()
            .Where(l => portfolioId == null || l.PortfolioId == portfolioId)
            .Join(
                latestDatePerPortfolio,
                l => new { l.PortfolioId, l.Date },
                p => new { p.PortfolioId, p.Date },
                (l, _) => l)
            .GroupBy(l => l.AssetClass)
            .Select(g => new { AssetClass = g.Key, ValuePln = g.Sum(l => l.ValuePln) })
            .ToListAsync(cancellationToken);

        var byAssetClass = assetClassTotals
            .Select(t => new DashboardAssetClassValue
            {
                AssetClass = t.AssetClass,
                ValuePln = t.ValuePln,
                Percentage = netWorthPln == 0m ? 0m : Math.Round(t.ValuePln / netWorthPln * 100m, 2),
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
