using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.RestoreAsset;

public sealed class RestoreAssetHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, TimeProvider timeProvider)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        // Two independent flags: an asset in an archived portfolio stays archived until the portfolio is restored.
        if (await dbContext.IsPortfolioArchivedAsync(portfolioId, cancellationToken))
        {
            return PortfolioErrors.Archived(portfolioId);
        }

        // Not archived: 200 with the unchanged body and no event.
        if (asset.IsArchived)
        {
            asset.IsArchived = false;

            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.ToFullResponseAsync(asset, WarsawCalendar.Today(timeProvider), cancellationToken);
    }
}
