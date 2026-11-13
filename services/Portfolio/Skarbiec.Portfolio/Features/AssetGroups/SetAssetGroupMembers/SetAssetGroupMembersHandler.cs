using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.AssetGroups.SetAssetGroupMembers;

public sealed class SetAssetGroupMembersHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid groupId, SetAssetGroupMembersRequest request, CancellationToken cancellationToken)
    {
        if (await dbContext.CheckAsync(groupId, cancellationToken) is { } groupError)
        {
            return groupError;
        }

        var listedAccounts = request.AccountIds.ToHashSet();
        var listedAssets = request.AssetIds.ToHashSet();

        var accounts = await dbContext.Accounts.ToDictionaryAsync(a => a.Id, cancellationToken);
        var assets = await dbContext.Assets.ToListAsync(cancellationToken);

        if (listedAccounts.Where(id => !accounts.ContainsKey(id)).Select(id => (Guid?)id).FirstOrDefault() is { } missingAccount)
        {
            return AccountErrors.NotFound(missingAccount);
        }

        var known = assets.ToDictionary(a => a.Id);
        if (listedAssets.Where(id => !known.ContainsKey(id)).Select(id => (Guid?)id).FirstOrDefault() is { } missingAsset)
        {
            return AssetErrors.NotFound(missingAsset);
        }

        if (listedAssets.Select(id => known[id]).FirstOrDefault(a => a.IsAccountCash()) is { } money)
        {
            return AssetGroupErrors.AccountCashManaged(money.Id);
        }

        var changed = new HashSet<Asset>();
        var byAccount = assets.Where(a => a.AccountId is not null).ToLookup(a => a.AccountId!.Value);

        foreach (var account in accounts.Values)
        {
            Guid? target = listedAccounts.Contains(account.Id) ? groupId : account.GroupId == groupId ? null : account.GroupId;
            if (target != account.GroupId)
            {
                changed.UnionWith(AccountGroupInheritance.ApplyAccountGroup(account, target, byAccount[account.Id]));
            }
        }

        foreach (var asset in assets.Where(a => !a.IsAccountCash()))
        {
            var listed = listedAssets.Contains(asset.Id);
            var effective = asset.GroupId;
            var moved = asset.AccountId is { } accountId
                ? listed && effective != groupId ? AccountGroupInheritance.Check(asset, accounts[accountId], groupId)
                    : !listed && effective == groupId && AccountGroupInheritance.Uncheck(asset, accounts[accountId], groupId)
                : listed && effective != groupId ? Assign(asset, groupId)
                    : !listed && effective == groupId && Assign(asset, null);

            if (moved)
            {
                changed.Add(asset);
            }
        }

        foreach (var asset in changed)
        {
            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static bool Assign(Asset asset, Guid? groupId)
    {
        asset.GroupId = groupId;

        return true;
    }
}
