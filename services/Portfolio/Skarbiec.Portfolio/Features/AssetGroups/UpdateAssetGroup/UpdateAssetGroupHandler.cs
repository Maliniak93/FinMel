using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.UpdateAssetGroup;

public sealed class UpdateAssetGroupHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AssetGroupResponse>> HandleAsync(
        Guid id, UpdateAssetGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await dbContext.AssetGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return AssetGroupErrors.NotFound(id);
        }

        var name = request.Name.Trim();

        var allocation = request.ClassAllocation ?? [];

        if (AssetGroupRules.ValidateFields(
                name, request.Color, request.TargetPln, request.Kind, request.MonthlyContributionPln, allocation) is { } invalid)
        {
            return invalid;
        }

        if (await dbContext.UniquenessErrorAsync(id, name, request.Color, request.Kind, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (group.Kind != request.Kind)
        {
            var otherRegular = await dbContext.AssetGroups
                .Where(g => g.Id != id && g.Kind == AssetGroupKind.Regular)
                .ToListAsync(cancellationToken);

            if (request.Kind == AssetGroupKind.Cushion)
            {
                var released = group.SharePercent ?? 0m;
                group.SharePercent = null;
                GroupShares.HandOver(released, otherRegular);
            }
            else
            {
                GroupShares.StartNewRegular(group, otherRegular);
            }
        }

        // No event: AssetPositionChanged carries only the group id, not its name, colour or target.
        group.Name = name;
        group.Color = request.Color;
        group.TargetPln = request.TargetPln;
        group.Kind = request.Kind;
        group.MonthlyContributionPln = request.MonthlyContributionPln;
        group.ClassAllocation = allocation;

        await dbContext.SaveChangesAsync(cancellationToken);

        var assetCount = await dbContext.Assets.CountAsync(a => a.GroupId == id, cancellationToken);

        return new AssetGroupResponse(
            group.Id, group.Name, group.Color, group.TargetPln, assetCount, group.Kind, group.MonthlyContributionPln, group.ClassAllocation,
            group.SharePercent);
    }
}
