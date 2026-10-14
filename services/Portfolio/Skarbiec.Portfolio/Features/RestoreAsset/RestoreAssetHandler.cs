using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.RestoreAsset;

public sealed class RestoreAssetHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid assetId, CancellationToken cancellationToken)
    {
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

        // An asset in an archived account stays archived until the account is restored.
        if (await dbContext.AccountArchivedErrorAsync(asset, cancellationToken) is { } accountArchived)
        {
            return accountArchived;
        }

        // Not archived: 200 with the unchanged body and no event.
        if (asset.IsArchived)
        {
            asset.IsArchived = false;

            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.ToFullResponseAsync(asset, cancellationToken);
    }
}
