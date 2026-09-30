using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.ArchiveAsset;

/// <summary>
/// Archives one asset of any class, at any balance and in any deposit status (asset-archive): it
/// becomes read-only and drops out of net worth from today, keeping every transaction and term.
/// </summary>
public sealed class ArchiveAssetHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, TimeProvider timeProvider)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: a stranger's or unknown asset is 404, and never learns the
        // portfolio's archived state.
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

        // Already archived: 200 with the unchanged body and no event, as ArchivePortfolio does — an
        // event is a fact that happened, and a repeated click moves nothing.
        if (!asset.IsArchived)
        {
            asset.IsArchived = true;

            // The flag is the fact (no new event type): the full position with IsArchived = true and a
            // bumped Version, in the same save as the flag (ADR-012, ADR-021).
            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.ToFullResponseAsync(asset, WarsawCalendar.Today(timeProvider), cancellationToken);
    }
}
