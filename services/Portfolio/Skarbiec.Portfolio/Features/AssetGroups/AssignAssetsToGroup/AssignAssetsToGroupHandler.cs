using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.AssignAssetsToGroup;

public sealed class AssignAssetsToGroupHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(AssignAssetsToGroupRequest request, CancellationToken cancellationToken)
    {
        var assetIds = request.AssetIds.Distinct().ToList();
        if (assetIds.Count == 0)
        {
            return AssetGroupErrors.AssetIdsRequired;
        }

        if (request.GroupId is { } groupId
            && !await dbContext.AssetGroups.AsNoTracking().AnyAsync(g => g.Id == groupId, cancellationToken))
        {
            return AssetGroupErrors.NotFound(groupId);
        }

        var assets = await dbContext.Assets.Where(a => assetIds.Contains(a.Id)).ToListAsync(cancellationToken);
        if (assets.Count != assetIds.Count)
        {
            var found = assets.Select(a => a.Id).ToHashSet();
            return AssetErrors.NotFound(assetIds.First(id => !found.Contains(id)));
        }

        // A group is a label: the account, cash place and archive guards do not apply.
        foreach (var asset in assets.Where(a => a.GroupId != request.GroupId))
        {
            asset.GroupId = request.GroupId;
            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
