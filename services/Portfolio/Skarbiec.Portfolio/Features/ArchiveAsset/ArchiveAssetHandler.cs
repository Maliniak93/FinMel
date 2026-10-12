using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.ArchiveAsset;

public sealed class ArchiveAssetHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, TimeProvider timeProvider)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid assetId, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first, so a stranger never learns an account's archived state.
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        if (asset.ManagedError() is { } cashManaged)
        {
            return cashManaged;
        }

        // An archived account is read-only as a whole.
        if (await dbContext.AccountArchivedErrorAsync(asset, cancellationToken) is { } accountArchived)
        {
            return accountArchived;
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
