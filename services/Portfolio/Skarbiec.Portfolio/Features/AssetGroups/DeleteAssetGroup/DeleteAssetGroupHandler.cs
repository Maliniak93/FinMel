using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.DeleteAssetGroup;

public sealed class DeleteAssetGroupHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await dbContext.AssetGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return AssetGroupErrors.NotFound(id);
        }

        var assets = await dbContext.Assets.Where(a => a.GroupId == id).ToListAsync(cancellationToken);

        // The group, the cleared assets and their position events commit together.
        foreach (var asset in assets)
        {
            asset.GroupId = null;
            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
        }

        foreach (var account in await dbContext.Accounts.Where(a => a.DefaultGroupId == id).ToListAsync(cancellationToken))
        {
            account.DefaultGroupId = null;
        }

        foreach (var place in await dbContext.CashPlaces.Where(p => p.DefaultGroupId == id).ToListAsync(cancellationToken))
        {
            place.DefaultGroupId = null;
        }

        if (group.Kind == AssetGroupKind.Regular)
        {
            var otherRegular = await dbContext.AssetGroups
                .Where(g => g.Id != id && g.Kind == AssetGroupKind.Regular)
                .ToListAsync(cancellationToken);
            GroupShares.HandOver(group.SharePercent ?? 0m, otherRegular);
        }

        dbContext.AssetGroups.Remove(group);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
