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

        if (AssetGroupRules.ValidateFields(name, request.Color, request.TargetPln) is { } invalid)
        {
            return invalid;
        }

        if (await dbContext.UniquenessErrorAsync(id, name, request.Color, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        // No event: AssetPositionChanged carries only the group id, not its name, colour or target.
        group.Name = name;
        group.Color = request.Color;
        group.TargetPln = request.TargetPln;

        await dbContext.SaveChangesAsync(cancellationToken);

        var assetCount = await dbContext.Assets.CountAsync(a => a.GroupId == id, cancellationToken);

        return new AssetGroupResponse(group.Id, group.Name, group.Color, group.TargetPln, assetCount);
    }
}
