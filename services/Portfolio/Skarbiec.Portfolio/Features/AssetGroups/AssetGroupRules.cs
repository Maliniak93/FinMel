using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups;

internal static class AssetGroupRules
{
    public static Error? ValidateFields(string name, string color, decimal? targetPln)
    {
        if (name.Length is 0 or > AssetGroupErrors.MaxNameLength)
        {
            return AssetGroupErrors.InvalidName;
        }

        if (!AssetGroupColors.Contains(color))
        {
            return AssetGroupErrors.InvalidColor;
        }

        if (targetPln is { } target
            && (target <= 0 || target > AssetGroupErrors.MaxTargetPln || decimal.Round(target, 2) != target))
        {
            return AssetGroupErrors.InvalidTarget;
        }

        return null;
    }

    // The group being edited is excluded, so renaming it to its own name in another case succeeds.
    public static async Task<Error?> UniquenessErrorAsync(
        this PortfolioDbContext dbContext, Guid? ownId, string name, string color, CancellationToken cancellationToken)
    {
        var lowerName = name.ToLowerInvariant();

        var others = dbContext.AssetGroups.AsNoTracking().Where(g => g.Id != ownId);

        if (await others.AnyAsync(g => g.Name.ToLower() == lowerName, cancellationToken))
        {
            return AssetGroupErrors.DuplicateName(name);
        }

        if (await others.AnyAsync(g => g.Color == color, cancellationToken))
        {
            return AssetGroupErrors.ColorTaken(color);
        }

        return null;
    }
}
