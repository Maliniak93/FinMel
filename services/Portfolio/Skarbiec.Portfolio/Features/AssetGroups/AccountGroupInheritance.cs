using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups;

// A component inherits its account's group unless it holds an exception; every method returns whether the effective group changed.
internal static class AccountGroupInheritance
{
    public static Guid? Effective(Account account, Asset asset) => asset.GroupIsException ? asset.GroupId : account.GroupId;

    public static bool Check(Asset asset, Account account, Guid groupId)
        => account.GroupId == groupId ? Inherit(asset, account) : Set(asset, groupId, true);

    public static bool Uncheck(Asset asset, Account account, Guid groupId)
    {
        if (Effective(account, asset) != groupId)
        {
            return false;
        }

        return asset.GroupIsException && account.GroupId != groupId ? Inherit(asset, account) : Set(asset, null, true);
    }

    // The assignment endpoint's null: drop whatever group the asset shows, as an exception.
    public static bool Clear(Asset asset, Account account)
        => Effective(account, asset) is not null && Set(asset, null, true);

    public static List<Asset> ApplyAccountGroup(Account account, Guid? groupId, IEnumerable<Asset> components)
    {
        account.GroupId = groupId;

        var changed = new List<Asset>();
        foreach (var component in components.Where(a => !a.GroupIsException && a.GroupId != groupId))
        {
            component.GroupId = groupId;
            changed.Add(component);
        }

        return changed;
    }

    public static bool Inherit(Asset asset, Account account) => Set(asset, account.GroupId, false);

    private static bool Set(Asset asset, Guid? groupId, bool isException)
    {
        var changed = asset.GroupId != groupId;
        asset.GroupId = groupId;
        asset.GroupIsException = isException;

        return changed;
    }
}
