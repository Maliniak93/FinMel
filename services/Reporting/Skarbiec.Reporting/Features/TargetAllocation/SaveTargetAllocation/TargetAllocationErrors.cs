using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;

internal static class TargetAllocationErrors
{
    public static readonly Error Sum =
        new("Validation.TargetAllocationSum", "The group targets must add up to exactly 100.");

    public static readonly Error AssetClassInManyGroups =
        new("Validation.AssetClassInManyGroups", "An asset class can belong to only one group.");

    public static readonly Error DuplicateGroupName =
        new("Validation.DuplicateGroupName", "Group names must be unique.");

    public static Error GroupWithoutAssetClass(string name) =>
        new("Validation.GroupWithoutAssetClass", $"Group '{name}' has no asset class.");

    public static Error InvalidGroup(string detail) =>
        new("Validation.InvalidGroup", detail);
}
