using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public sealed class UpdateAssetHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AssetResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        var manualValue = Money.Create(request.ManualValue, request.Currency);
        if (manualValue.IsFailure)
        {
            return manualValue.Error;
        }

        asset.AssetClass = request.AssetClass;
        asset.Name = request.Name;
        asset.Currency = request.Currency;
        asset.Quantity = request.Quantity;
        asset.ManualValueAmount = manualValue.Value.Amount;
        asset.ManualValueDate = request.ManualValueDate;

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse();
    }
}
