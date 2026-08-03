using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetWealthSummary;

// Phase 2: replaced by Reporting (T2.12/T2.13) — this aggregate is a Phase 1 shortcut computed
// straight from Portfolio's own data (ADR-015: "where [a REST fan-out] hurts (the dashboard), the
// ready Reporting read model answers" — Reporting doesn't exist yet in Phase 1).
public sealed class GetWealthSummaryHandler(PortfolioDbContext dbContext)
{
    public async Task<WealthSummaryResponse> HandleAsync(CancellationToken cancellationToken)
    {
        // Archived portfolios are deliberately hidden by the user (ListPortfolios' own
        // includeArchived=false default) — net worth follows the same default.
        var portfolios = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .ToListAsync(cancellationToken);

        var portfolioIds = portfolios.Select(p => p.Id).ToList();

        var assets = await dbContext.Assets
            .AsNoTracking()
            .Where(a => portfolioIds.Contains(a.PortfolioId))
            .ToListAsync(cancellationToken);

        var totalNetWorth = assets.Sum(a => a.ManualValueAmount);

        var byAssetClass = assets
            .GroupBy(a => a.AssetClass)
            .Select(g => new AssetClassBreakdown
            {
                AssetClass = g.Key,
                Value = g.Sum(a => a.ManualValueAmount),
                Percentage = totalNetWorth == 0m ? 0m : Math.Round(g.Sum(a => a.ManualValueAmount) / totalNetWorth * 100m, 2),
            })
            .OrderByDescending(b => b.Value)
            .ToList();

        var byPortfolio = portfolios
            .Select(p =>
            {
                var portfolioAssets = assets.Where(a => a.PortfolioId == p.Id);
                return new PortfolioBreakdown
                {
                    PortfolioId = p.Id,
                    Name = p.Name,
                    Value = portfolioAssets.Sum(a => a.ManualValueAmount),
                };
            })
            .OrderByDescending(b => b.Value)
            .ToList();

        return new WealthSummaryResponse
        {
            TotalNetWorth = totalNetWorth,
            BaseCurrency = "PLN",
            FxConversionIsNaive = true,
            ByAssetClass = byAssetClass,
            ByPortfolio = byPortfolio,
        };
    }
}
