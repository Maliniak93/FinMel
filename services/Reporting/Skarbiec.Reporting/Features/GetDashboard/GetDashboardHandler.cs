using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.GetDashboard;

public sealed class GetDashboardHandler(ReportingDbContext db)
{
    public async Task<DashboardResponse> HandleAsync(CancellationToken cancellationToken)
    {
        // Snapshots and lines are tenancy-filtered in their own right, so this never reaches another user's rows.
        var snapshot = await db.ValuationSnapshots
            .AsNoTracking()
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return new DashboardResponse
            {
                NetWorthPln = 0m,
                AsOf = null,
                IsStale = false,
                ByAssetClass = [],
                ByPortfolio = [],
            };
        }

        var netWorthPln = snapshot.TotalPln;

        var lines = await db.AssetValuations
            .AsNoTracking()
            .Where(l => l.Date == snapshot.Date)
            .Select(l => new { l.PortfolioId, l.AssetClass, l.ValuePln, l.IsStale })
            .ToListAsync(cancellationToken);

        var byPortfolio = lines
            .GroupBy(l => l.PortfolioId)
            .Select(g =>
            {
                var valuePln = g.Sum(l => l.ValuePln);
                return new DashboardPortfolioValue
                {
                    PortfolioId = g.Key,
                    ValuePln = valuePln,
                    Percentage = netWorthPln == 0m ? 0m : Math.Round(valuePln / netWorthPln * 100m, 2),
                    SnapshotDate = snapshot.Date,
                    IsStale = g.Any(l => l.IsStale),
                };
            })
            .OrderByDescending(p => p.ValuePln)
            .ToList();

        var byAssetClass = lines
            .GroupBy(l => l.AssetClass)
            .Select(g =>
            {
                var valuePln = g.Sum(l => l.ValuePln);
                return new DashboardAssetClassValue
                {
                    AssetClass = g.Key,
                    ValuePln = valuePln,
                    Percentage = netWorthPln == 0m ? 0m : Math.Round(valuePln / netWorthPln * 100m, 2),
                };
            })
            .OrderByDescending(b => b.ValuePln)
            .ToList();

        return new DashboardResponse
        {
            NetWorthPln = netWorthPln,
            AsOf = snapshot.Date,
            IsStale = snapshot.IsStale,
            ByAssetClass = byAssetClass,
            ByPortfolio = byPortfolio,
        };
    }
}
