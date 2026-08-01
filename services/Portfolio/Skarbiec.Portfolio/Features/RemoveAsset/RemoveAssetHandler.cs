using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RemoveAsset;

public sealed class RemoveAssetHandler(PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint)
{
    public async Task<Result> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        if (asset.TransactionCount > 0)
        {
            return AssetErrors.HasTransactions(assetId);
        }

        var portfolio = await dbContext.Portfolios.FirstAsync(p => p.Id == portfolioId, cancellationToken);
        portfolio.AssetCount--;

        dbContext.Assets.Remove(asset);

        await publishEndpoint.Publish(new AssetChanged
        {
            AssetId = asset.Id,
            PortfolioId = portfolioId,
            UserId = dbContext.CurrentUserId,
            Kind = AssetChangeKind.Removed
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
