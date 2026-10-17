using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.TargetAllocation;

public static class TargetAllocationMath
{
    public static TargetAllocationComputation Compute(
        IReadOnlyList<TargetAllocationGroup> groups, IReadOnlyDictionary<AssetClass, decimal> valuesByClass)
    {
        var groupValues = groups.Select(g => g.AssetClasses.Sum(c => valuesByClass.GetValueOrDefault(c))).ToList();
        var basePln = groupValues.Sum();

        var groupedClasses = groups.SelectMany(g => g.AssetClasses).ToHashSet();
        var outside = valuesByClass.Where(v => !groupedClasses.Contains(v.Key)).ToList();

        var targetValues = basePln > 0m ? SplitTargets(groups, basePln) : null;

        var results = new List<TargetAllocationGroupComputation>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var low = Math.Max(0m, group.TargetPercent - group.BandPp);
            var high = Math.Min(100m, group.TargetPercent + group.BandPp);

            if (targetValues is null)
            {
                results.Add(new TargetAllocationGroupComputation(groupValues[i], null, null, low, high, null, null, null));
                continue;
            }

            var currentPercent = Math.Round(groupValues[i] / basePln * 100m, 1, MidpointRounding.AwayFromZero);
            results.Add(new TargetAllocationGroupComputation(
                groupValues[i],
                currentPercent,
                currentPercent - group.TargetPercent,
                low,
                high,
                targetValues[i],
                groupValues[i] - targetValues[i],
                currentPercent >= low && currentPercent <= high));
        }

        return new TargetAllocationComputation(
            basePln,
            outside.Sum(v => v.Value),
            outside.Where(v => v.Value != 0m).Select(v => v.Key).ToList(),
            results);
    }

    // Largest remainder over whole grosz, ties by group order, so the targets add up to the base exactly.
    private static decimal[] SplitTargets(IReadOnlyList<TargetAllocationGroup> groups, decimal basePln)
    {
        var baseCents = Math.Round(basePln * 100m, MidpointRounding.AwayFromZero);
        var exact = groups.Select(g => g.TargetPercent * baseCents / 100m).ToArray();
        var cents = exact.Select(Math.Floor).ToArray();

        var leftover = (int)Math.Max(0m, baseCents - cents.Sum());
        var byRemainder = Enumerable.Range(0, groups.Count)
            .OrderByDescending(i => exact[i] - cents[i])
            .ThenBy(i => i)
            .Take(leftover);
        foreach (var index in byRemainder)
        {
            cents[index]++;
        }

        return cents.Select(c => c / 100m).ToArray();
    }
}

public sealed record TargetAllocationComputation(
    decimal BasePln,
    decimal OutsidePln,
    IReadOnlyList<AssetClass> OutsideAssetClasses,
    IReadOnlyList<TargetAllocationGroupComputation> Groups);

public sealed record TargetAllocationGroupComputation(
    decimal ValuePln,
    decimal? CurrentPercent,
    decimal? DeviationPp,
    decimal BandLowPercent,
    decimal BandHighPercent,
    decimal? TargetValuePln,
    decimal? DeviationPln,
    bool? IsInBand);
