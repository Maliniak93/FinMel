using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

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

        if (assets.FirstOrDefault(a => a.IsAccountCash()) is { } money)
        {
            return AssetGroupErrors.AccountCashManaged(money.Id);
        }

        var accountIds = assets.Where(a => a.AccountId is not null).Select(a => a.AccountId!.Value).Distinct().ToList();
        var accounts = await dbContext.Accounts.Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);

        // A group is a label: the account, cash place and archive guards do not apply.
        foreach (var asset in assets)
        {
            var changed = asset.AccountId is { } accountId
                ? request.GroupId is { } target
                    ? AccountGroupInheritance.Check(asset, accounts[accountId], target)
                    : AccountGroupInheritance.Clear(asset, accounts[accountId])
                : Assign(asset, request.GroupId);

            if (changed)
            {
                await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static bool Assign(Asset asset, Guid? groupId)
    {
        var changed = asset.GroupId != groupId;
        asset.GroupId = groupId;

        return changed;
    }
}
