using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups;

internal static class AssetGroupRules
{
    public static Error? ValidateFields(
        string name,
        string color,
        decimal? targetPln,
        AssetGroupKind kind,
        decimal monthlyContributionPln,
        List<AssetGroupClassShare> classAllocation)
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

        if (kind == AssetGroupKind.Cushion && targetPln is not > 0)
        {
            return AssetGroupErrors.TargetRequiredForCushion;
        }

        if (monthlyContributionPln is < 0 or > AssetGroupErrors.MaxMonthlyContributionPln
            || decimal.Round(monthlyContributionPln, 2) != monthlyContributionPln)
        {
            return AssetGroupErrors.InvalidContribution;
        }

        return ValidateAllocation(classAllocation);
    }

    private static Error? ValidateAllocation(List<AssetGroupClassShare> allocation)
    {
        if (allocation.Count == 0)
        {
            return null;
        }

        if (allocation.Any(s => !Enum.IsDefined(s.AssetClass) || s.Percent is <= 0 or > 100 || decimal.Round(s.Percent, 1) != s.Percent)
            || allocation.Select(s => s.AssetClass).Distinct().Count() != allocation.Count)
        {
            return AssetGroupErrors.InvalidClassAllocation;
        }

        return allocation.Sum(s => s.Percent) == 100m ? null : AssetGroupErrors.ClassAllocationSum;
    }

    // The group being edited is excluded, so renaming it to its own name in another case succeeds.
    public static async Task<Error?> UniquenessErrorAsync(
        this PortfolioDbContext dbContext,
        Guid? ownId,
        string name,
        string color,
        AssetGroupKind kind,
        CancellationToken cancellationToken)
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

        if (kind == AssetGroupKind.Cushion && await others.AnyAsync(g => g.Kind == AssetGroupKind.Cushion, cancellationToken))
        {
            return AssetGroupErrors.CushionGroupExists;
        }

        return null;
    }
}
