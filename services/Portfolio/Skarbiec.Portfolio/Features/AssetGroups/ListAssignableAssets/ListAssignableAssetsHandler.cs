using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.ListAssignableAssets;

public sealed class ListAssignableAssetsHandler(PortfolioDbContext dbContext)
{
    public async Task<List<AssignableAssetResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets
            .AsNoTracking()
            .Select(a => new AssignableAssetResponse(
                a.Id,
                a.Name,
                a.AssetClass,
                a.Currency,
                a.AccountId,
                dbContext.Accounts.Where(x => x.Id == a.AccountId).Select(x => x.Name).FirstOrDefault(),
                dbContext.CashPlaces.Where(x => x.Id == a.CashPlaceId).Select(x => x.Name).FirstOrDefault(),
                a.GroupId,
                a.IsArchived))
            .ToListAsync(cancellationToken);

        return [.. assets
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Currency, StringComparer.Ordinal)
            .ThenBy(a => a.AssetId)];
    }
}
