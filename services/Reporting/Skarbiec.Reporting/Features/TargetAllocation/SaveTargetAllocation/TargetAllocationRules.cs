using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;

internal static class TargetAllocationRules
{
    private const int MaxNameLength = 50;

    public static List<TargetAllocationGroup> Normalize(SaveTargetAllocationRequest request) =>
        request.Groups
            .Select(g => new TargetAllocationGroup(g.Name?.Trim() ?? string.Empty, g.AssetClasses?.ToList() ?? [], g.TargetPercent, g.BandPp))
            .ToList();

    public static Error? Validate(List<TargetAllocationGroup> groups)
    {
        foreach (var group in groups)
        {
            if (group.Name.Length is 0 or > MaxNameLength)
            {
                return TargetAllocationErrors.InvalidGroup($"A group name must be 1-{MaxNameLength} characters.");
            }

            if (!IsOneDecimalPercent(group.TargetPercent) || !IsOneDecimalPercent(group.BandPp))
            {
                return TargetAllocationErrors.InvalidGroup($"Group '{group.Name}': target and band must be 0-100 with at most one decimal.");
            }

            if (group.AssetClasses.Any(c => !Enum.IsDefined(c)))
            {
                return TargetAllocationErrors.InvalidGroup($"Group '{group.Name}' has an undefined asset class.");
            }
        }

        var empty = groups.FirstOrDefault(g => g.AssetClasses.Count == 0);
        if (empty is not null)
        {
            return TargetAllocationErrors.GroupWithoutAssetClass(empty.Name);
        }

        if (groups.Select(g => g.Name.ToUpperInvariant()).Distinct().Count() != groups.Count)
        {
            return TargetAllocationErrors.DuplicateGroupName;
        }

        if (groups.SelectMany(g => g.AssetClasses).GroupBy(c => c).Any(c => c.Count() > 1))
        {
            return TargetAllocationErrors.AssetClassInManyGroups;
        }

        return groups.Sum(g => g.TargetPercent) == 100m ? null : TargetAllocationErrors.Sum;
    }

    private static bool IsOneDecimalPercent(decimal value) =>
        value is >= 0m and <= 100m && value == Math.Round(value, 1);
}
