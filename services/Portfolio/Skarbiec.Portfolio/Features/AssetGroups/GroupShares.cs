using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups;

// Keeps the Regular groups' shares summing to 100 whenever there are any; the callers save in the same SaveChanges.
internal static class GroupShares
{
    public static void StartNewRegular(AssetGroup group, IReadOnlyList<AssetGroup> otherRegular) =>
        group.SharePercent = otherRegular.Count == 0 ? 100m : 0m;

    // Floors each part to one decimal; the remainder goes to the largest share, ties by name.
    public static void HandOver(decimal released, IReadOnlyList<AssetGroup> rest)
    {
        if (rest.Count == 0 || released <= 0m)
        {
            return;
        }

        var total = rest.Sum(g => g.SharePercent ?? 0m);
        var parts = rest
            .Select(g => (Group: g, Part: total == 0m ? 0m : Math.Floor((g.SharePercent ?? 0m) / total * released * 10m) / 10m))
            .ToList();

        var largest = rest
            .OrderByDescending(g => g.SharePercent ?? 0m)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .First();
        var remainder = released - parts.Sum(p => p.Part);

        foreach (var (group, part) in parts)
        {
            group.SharePercent = (group.SharePercent ?? 0m) + part + (group == largest ? remainder : 0m);
        }
    }
}
