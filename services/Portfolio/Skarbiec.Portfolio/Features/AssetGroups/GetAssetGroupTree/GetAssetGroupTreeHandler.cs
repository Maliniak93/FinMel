using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.AssetGroups.GetAssetGroupTree;

public sealed class GetAssetGroupTreeHandler(PortfolioDbContext dbContext)
{
    public async Task<List<AssetGroupTreeItem>> HandleAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var assets = await dbContext.Assets.AsNoTracking().ToListAsync(cancellationToken);
        var byAccount = assets.Where(a => a.AccountId is not null).ToLookup(a => a.AccountId!.Value);

        var items = new List<AssetGroupTreeItem>();
        foreach (var account in accounts)
        {
            var own = byAccount[account.Id].ToList();
            var cash = own.First(a => AccountCash.MoneyClasses.Contains(a.AssetClass));
            var children = own
                .Where(a => a.Id != cash.Id)
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Id)
                .Select(a => new AssetGroupTreeChild(a.Id, a.Name, a.AssetClass, a.GroupId, a.GroupIsException, a.IsArchived))
                .ToList();

            items.Add(new AssetGroupTreeItem(
                AssetGroupTreeItemKind.Account,
                account.Id,
                null,
                account.Name,
                account.Type,
                null,
                account.GroupId,
                cash.Id,
                account.IsArchived,
                children));
        }

        items.AddRange(assets.Where(a => a.AccountId is null).Select(a => new AssetGroupTreeItem(
            AssetGroupTreeItemKind.Asset, null, a.Id, a.Name, null, a.AssetClass, a.GroupId, null, a.IsArchived, [])));

        return [.. items
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.AccountId ?? i.AssetId)];
    }
}
