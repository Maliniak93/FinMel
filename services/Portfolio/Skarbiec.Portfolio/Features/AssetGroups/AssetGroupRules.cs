using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.AssetGroups;

internal static class AssetGroupRules
{
    public static Error? ValidateFields(
        string name,
        string color,
        decimal? targetPln,
        AssetGroupKind kind,
        List<GroupContribution> contributions,
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

        if (contributions.Count > AssetGroupErrors.MaxContributions)
        {
            return AssetGroupErrors.TooManyContributions;
        }

        if (contributions.Any(c => c.AmountPln is <= 0 or > AssetGroupErrors.MaxMonthlyContributionPln
            || decimal.Round(c.AmountPln, 2) != c.AmountPln))
        {
            return AssetGroupErrors.InvalidContribution;
        }

        return ValidateAllocation(classAllocation);
    }

    // An archived account is accepted only when the group already had it, so an untouched contribution survives an edit.
    public static async Task<Error?> ContributionAccountErrorAsync(
        this PortfolioDbContext dbContext,
        List<GroupContribution> contributions,
        IReadOnlyCollection<Guid?> keptAccountIds,
        CancellationToken cancellationToken)
    {
        var accountIds = contributions.Where(c => c.AccountId is not null).Select(c => c.AccountId!.Value).Distinct().ToList();
        if (accountIds.Count == 0)
        {
            return null;
        }

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.IsArchived })
            .ToListAsync(cancellationToken);

        foreach (var accountId in accountIds)
        {
            var account = accounts.FirstOrDefault(a => a.Id == accountId);
            if (account is null)
            {
                return AccountErrors.NotFound(accountId);
            }

            if (account.IsArchived && !keptAccountIds.Contains(accountId))
            {
                return AssetGroupErrors.ContributionAccountArchived(accountId);
            }
        }

        return null;
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
