using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals.GetMetal;

public sealed class GetMetalHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<MetalResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // A MetalHolding row exists only for a PreciousMetal asset, so any other asset id misses here.
        var row = await (
                from holding in dbContext.MetalHoldings.AsNoTracking()
                join asset in dbContext.Assets on holding.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.Id == assetId && asset.PortfolioId == portfolioId
                select new { Holding = holding, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return MetalErrors.NotFound(assetId);
        }

        return row.Holding.ToResponse(row.Asset, row.PortfolioName, row.PortfolioIsArchived);
    }
}
