using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.ListAssetGroups;

public sealed class ListAssetGroupsHandler(PortfolioDbContext dbContext)
{
    public async Task<List<AssetGroupResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var groups = await dbContext.AssetGroups
            .AsNoTracking()
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.Color,
                g.TargetPln,
                g.Kind,
                g.MonthlyContributionPln,
                g.ClassAllocation,
                AssetCount = dbContext.Assets.Count(a => a.GroupId == g.Id)
            })
            .ToListAsync(cancellationToken);

        return [.. groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AssetGroupResponse(
                g.Id, g.Name, g.Color, g.TargetPln, g.AssetCount, g.Kind, g.MonthlyContributionPln, g.ClassAllocation))];
    }
}
