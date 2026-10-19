using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.AssetGroups;

internal static class AssetGroupErrors
{
    public const int MaxGroups = 12;

    public const int MaxNameLength = 50;

    public const decimal MaxTargetPln = 1_000_000_000m;

    public static Error NotFound(Guid id) =>
        new("NotFound.AssetGroup", $"Asset group '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicateName", $"An asset group named '{name}' already exists.");

    public static Error GroupLimitReached { get; } =
        new("Conflict.GroupLimitReached", $"At most {MaxGroups} asset groups are allowed.");

    public static Error ColorTaken(string color) =>
        new("Conflict.GroupColorTaken", $"Another asset group already uses the colour '{color}'.");

    public static Error InvalidName { get; } =
        new("Validation.InvalidName", $"The name must be 1 to {MaxNameLength} characters.");

    public static Error InvalidColor { get; } =
        new("Validation.InvalidColor", $"Accepted colours: {string.Join(", ", AssetGroupColors.Keys)}.");

    public static Error InvalidTarget { get; } =
        new("Validation.InvalidTarget", $"The target must be above 0, have at most 2 decimals and not exceed {MaxTargetPln:0}.");

    public static Error AssetIdsRequired { get; } =
        new("Validation.AssetIdsRequired", "Pick at least one asset.");
}
