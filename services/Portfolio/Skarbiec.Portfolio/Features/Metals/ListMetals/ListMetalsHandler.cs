using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals.ListMetals;

public sealed class ListMetalsHandler(PortfolioDbContext dbContext)
{
    public async Task<IReadOnlyList<MetalResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from holding in dbContext.MetalHoldings.AsNoTracking()
                join asset in dbContext.Assets on holding.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                orderby portfolio.Name, asset.Name
                select new { Holding = holding, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => r.Holding.ToResponse(r.Asset, r.PortfolioName, r.PortfolioIsArchived))
            .ToList();
    }
}
