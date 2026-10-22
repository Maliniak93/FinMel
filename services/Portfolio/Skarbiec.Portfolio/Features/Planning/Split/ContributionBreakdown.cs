using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Planning.Split;

public sealed record ClassAmount(AssetClass AssetClass, decimal AmountPln);

public static class ContributionBreakdown
{
    // The contribution goes to the classes below their target value in proportion to the gaps; with no assets or no gap it follows the percentages.
    public static IReadOnlyList<ClassAmount> Compute(
        decimal contributionPln,
        IReadOnlyList<AssetGroupClassShare> allocation,
        IReadOnlyDictionary<AssetClass, decimal> valuesPln)
    {
        if (allocation.Count == 0)
        {
            return [];
        }

        var total = valuesPln.Values.Sum();
        var gaps = allocation
            .Select(s => Math.Max(0m, s.Percent / 100m * total - valuesPln.GetValueOrDefault(s.AssetClass)))
            .ToList();

        var weights = total > 0m && gaps.Sum() > 0m ? gaps : [.. allocation.Select(s => s.Percent)];
        var weightSum = weights.Sum();

        var parts = allocation
            .Select((s, i) => (s.AssetClass, Amount: Math.Round(contributionPln * weights[i] / weightSum, 2, MidpointRounding.AwayFromZero)))
            .ToList();

        var largest = parts.IndexOf(parts.MaxBy(p => p.Amount));
        parts[largest] = (parts[largest].AssetClass, parts[largest].Amount + contributionPln - parts.Sum(p => p.Amount));

        return [.. parts.Select(p => new ClassAmount(p.AssetClass, p.Amount))];
    }
}
