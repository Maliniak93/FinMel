using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed class AddAssetHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, AddAssetRequest request, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(portfolioId);
        }

        var manualValue = Money.Create(request.ManualValue, request.Currency);
        if (manualValue.IsFailure)
        {
            return manualValue.Error;
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            AssetClass = request.AssetClass,
            Name = request.Name,
            Currency = request.Currency,
            Quantity = request.Quantity,
            ManualValueAmount = manualValue.Value.Amount,
            ManualValueDate = request.ManualValueDate
        };

        dbContext.Assets.Add(asset);
        portfolio.AssetCount++;

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse();
    }
}
