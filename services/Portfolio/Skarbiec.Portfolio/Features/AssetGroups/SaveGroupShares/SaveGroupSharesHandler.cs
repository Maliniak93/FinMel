using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.SaveGroupShares;

public sealed class SaveGroupSharesHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(SaveGroupSharesRequest request, CancellationToken cancellationToken)
    {
        if (request.Shares.Any(s => s.SharePercent is < 0 or > 100 || decimal.Round(s.SharePercent, 1) != s.SharePercent))
        {
            return AssetGroupErrors.InvalidShare;
        }

        var regular = await dbContext.AssetGroups
            .Where(g => g.Kind == AssetGroupKind.Regular)
            .ToListAsync(cancellationToken);

        var requested = request.Shares.Select(s => s.GroupId).ToList();
        if (requested.Distinct().Count() != requested.Count
            || !requested.ToHashSet().SetEquals(regular.Select(g => g.Id)))
        {
            return AssetGroupErrors.SharesGroupMismatch;
        }

        if (regular.Count > 0 && request.Shares.Sum(s => s.SharePercent) != 100m)
        {
            return AssetGroupErrors.SharesSum;
        }

        foreach (var group in regular)
        {
            group.SharePercent = request.Shares.Single(s => s.GroupId == group.Id).SharePercent;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
