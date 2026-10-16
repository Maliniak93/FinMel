using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AllocationPlan;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.Planning.Allocation;

internal sealed record CushionFigures(decimal BalanceStart, decimal Target, int TargetMonths, decimal BalanceEnd, bool Reached);

internal sealed record LedgerBucket(AllocationPlanEntry Entry, BucketAllocation Allocation, CushionFigures? Cushion);

internal sealed record LedgerMonth(AllocationPlanVersion? InForce, IReadOnlyList<LedgerBucket> Buckets, decimal Deficit);

internal static class AllocationLedger
{
    // Balances depend on every earlier month's split, so a plan with a cushion is walked from its first version.
    public static YearMonth WalkStart(YearMonth viewed, IReadOnlyList<AllocationPlanVersion> versions)
    {
        var hasCushion = versions.Any(v => v.Entries.Any(e => e.TargetMonths is not null));
        if (!hasCushion)
        {
            return viewed;
        }

        var first = YearMonth.From(versions.Min(v => v.EffectiveFrom));

        return first < viewed ? first : viewed;
    }

    public static LedgerMonth Compute(
        YearMonth viewed,
        IReadOnlyList<AllocationPlanVersion> versions,
        IReadOnlyList<AllocationBalanceOverride> overrides,
        Func<YearMonth, MonthFigures> figuresFor)
    {
        var month = WalkStart(viewed, versions);
        var balances = overrides
            .Where(o => o.Month < month.FirstDay)
            .GroupBy(o => o.BucketId)
            .ToDictionary(g => g.Key, g => g.MaxBy(o => o.Month)!.Amount);
        var overridesByMonth = overrides.ToLookup(o => o.Month);
        LedgerMonth result;

        while (true)
        {
            foreach (var balanceOverride in overridesByMonth[month.FirstDay])
            {
                balances[balanceOverride.BucketId] = balanceOverride.Amount;
            }

            result = Step(versions.InForce(month), figuresFor(month), balances);
            if (month == viewed)
            {
                return result;
            }

            month = month.AddMonths(1);
        }
    }

    private static LedgerMonth Step(AllocationPlanVersion? inForce, MonthFigures figures, Dictionary<Guid, decimal> balances)
    {
        var entries = (inForce?.Entries ?? [])
            .OrderBy(e => e.TargetMonths is null)
            .ThenBy(e => e.ShareType)
            .ThenBy(e => e.Position)
            .ToList();
        var inputs = entries
            .Select(e => e.TargetMonths is { } months
                ? BucketInput.Cushion(balances.GetValueOrDefault(e.BucketId), months * figures.Cost)
                : new BucketInput(e.ShareType!.Value, e.Share!.Value, e.IsRemainder))
            .ToList();
        var result = AllocationCalculator.Allocate(figures.Forecast, inputs);

        var buckets = new List<LedgerBucket>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            CushionFigures? cushion = null;
            if (entry.TargetMonths is { } months)
            {
                var start = balances.GetValueOrDefault(entry.BucketId);
                var end = start + result.Buckets[i].Allocated;
                var target = months * figures.Cost;
                balances[entry.BucketId] = end;
                cushion = new CushionFigures(start, target, months, end, target - end < AllocationCalculator.CushionTolerance);
            }

            buckets.Add(new LedgerBucket(entry, result.Buckets[i], cushion));
        }

        return new LedgerMonth(inForce, buckets, result.Deficit);
    }
}
