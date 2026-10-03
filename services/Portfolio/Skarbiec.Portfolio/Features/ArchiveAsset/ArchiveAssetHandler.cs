using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.ArchiveAsset;

public sealed class ArchiveAssetHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, TimeProvider timeProvider)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first, so a stranger never learns the portfolio's archived state.
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        // An archived portfolio is read-only as a whole, the asset's own flag included.
        if (await dbContext.IsPortfolioArchivedAsync(portfolioId, cancellationToken))
        {
            return PortfolioErrors.Archived(portfolioId);
        }

        // Already archived: 200 with no event, since a repeated click moves nothing.
        if (!asset.IsArchived)
        {
            asset.IsArchived = true;

            // The flag is the fact: the full position with IsArchived set, in the same save.
            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.ToFullResponseAsync(asset, WarsawCalendar.Today(timeProvider), cancellationToken);
    }
}
