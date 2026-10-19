using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups;

internal static class AssetGroupGuard
{
    public static async Task<Error?> CheckAsync(this PortfolioDbContext dbContext, Guid? groupId, CancellationToken cancellationToken)
    {
        if (groupId is not { } id)
        {
            return null;
        }

        return await dbContext.AssetGroups.AsNoTracking().AnyAsync(g => g.Id == id, cancellationToken)
            ? null
            : AssetGroupErrors.NotFound(id);
    }
}
