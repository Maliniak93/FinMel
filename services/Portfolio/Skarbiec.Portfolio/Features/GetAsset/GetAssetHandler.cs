using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetAsset;

public sealed class GetAssetHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        return asset is null
            ? AssetErrors.NotFound(assetId)
            : asset.ToResponse();
    }
}
