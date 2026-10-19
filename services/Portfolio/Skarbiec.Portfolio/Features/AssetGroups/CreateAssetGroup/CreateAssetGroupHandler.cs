using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.CreateAssetGroup;

public sealed class CreateAssetGroupHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AssetGroupResponse>> HandleAsync(CreateAssetGroupRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (AssetGroupRules.ValidateFields(name, request.Color, request.TargetPln) is { } invalid)
        {
            return invalid;
        }

        if (await dbContext.AssetGroups.CountAsync(cancellationToken) >= AssetGroupErrors.MaxGroups)
        {
            return AssetGroupErrors.GroupLimitReached;
        }

        if (await dbContext.UniquenessErrorAsync(null, name, request.Color, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var group = new AssetGroup { Id = Guid.NewGuid(), Name = name, Color = request.Color, TargetPln = request.TargetPln };

        dbContext.AssetGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssetGroupResponse(group.Id, group.Name, group.Color, group.TargetPln, 0);
    }
}
