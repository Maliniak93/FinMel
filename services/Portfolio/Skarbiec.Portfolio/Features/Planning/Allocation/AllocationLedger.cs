using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AllocationPlan;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.Planning.Allocation;

internal sealed record CushionFigures(decimal BalanceStart, decimal Target, int TargetMonths, decimal BalanceEnd, bool Reached);

internal sealed record LimitFigures(int Year, decimal Limit, decimal ContributedStart, decimal ContributedEnd, bool Reached, bool Estimated);

internal sealed record LedgerBucket(AllocationPlanEntry Entry, BucketAllocation Allocation, CushionFigures? Cushion, LimitFigures? Limit);

internal sealed record LedgerMonth(AllocationPlanVersion? InForce, IReadOnlyList<LedgerBucket> Buckets, decimal Deficit);

internal static class AllocationLedger
{
    // Balances depend on every earlier month's split, so a plan with a cushion is walked from its first version;
    // a plan with limited buckets only from January of the viewed year, where their counters restart.
    public static YearMonth WalkStart(YearMonth viewed, IReadOnlyList<AllocationPlanVersion> versions, IReadOnlyDictionary<Guid, BucketKind> kinds)
    {
        var hasCushion = versions.Any(v => v.Entries.Any(e => e.TargetMonths is not null));
        var hasLimited = versions.Any(v => v.Entries.Any(e => kinds[e.BucketId].IsLimited()));
        if (!hasCushion && !hasLimited)
        {
            return viewed;
        }

        var first = YearMonth.From(versions.Min(v => v.EffectiveFrom));
        var januaryOfViewed = new YearMonth(viewed.Year, 1);
        var start = hasCushion || first > januaryOfViewed ? first : januaryOfViewed;

        return start < viewed ? start : viewed;
    }

    public static LedgerMonth Compute(
        YearMonth viewed,
        IReadOnlyList<AllocationPlanVersion> versions,
        IReadOnlyList<AllocationBalanceOverride> overrides,
        IReadOnlyDictionary<Guid, BucketKind> kinds,
        Func<YearMonth, MonthFigures> figuresFor)
    {
        var month = WalkStart(viewed, versions, kinds);
        var balances = overrides
            .Where(o => o.Month < month.FirstDay && (!kinds[o.BucketId].IsLimited() || o.Month.Year == month.Year))
            .GroupBy(o => o.BucketId)
            .ToDictionary(g => g.Key, g => g.MaxBy(o => o.Month)!.Amount);
        var overridesByMonth = overrides.ToLookup(o => o.Month);
        LedgerMonth result;

        while (true)
        {
            if (month.Month == 1)
            {
                foreach (var counter in balances.Keys.Where(id => kinds[id].IsLimited()).ToList())
                {
                    balances.Remove(counter);
                }
            }

            foreach (var balanceOverride in overridesByMonth[month.FirstDay])
            {
                balances[balanceOverride.BucketId] = balanceOverride.Amount;
            }

            result = Step(month, versions.InForce(month), figuresFor(month), kinds, balances);
            if (month == viewed)
            {
                return result;
            }

            month = month.AddMonths(1);
        }
    }

    private static LedgerMonth Step(
        YearMonth month,
        AllocationPlanVersion? inForce,
        MonthFigures figures,
        IReadOnlyDictionary<Guid, BucketKind> kinds,
        Dictionary<Guid, decimal> balances)
    {
        var entries = (inForce?.Entries ?? [])
            .OrderBy(e => e.TargetMonths is null)
            .ThenBy(e => e.ShareType)
            .ThenBy(e => e.Position)
            .ToList();
        var limits = StatutoryLimits.For(month.Year);
        var inputs = entries
            .Select(e =>
            {
                if (e.TargetMonths is { } months)
                {
                    return BucketInput.Cushion(balances.GetValueOrDefault(e.BucketId), months * figures.Cost);
                }

                var kind = kinds[e.BucketId];

                return kind.IsLimited()
                    ? BucketInput.Limited(
                        e.ShareType!.Value, e.Share!.Value, e.IsRemainder, Math.Max(limits.For(kind) - balances.GetValueOrDefault(e.BucketId), 0m))
                    : new BucketInput(e.ShareType!.Value, e.Share!.Value, e.IsRemainder);
            })
            .ToList();
        var result = AllocationCalculator.Allocate(figures.Forecast, inputs);

        var buckets = new List<LedgerBucket>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var start = balances.GetValueOrDefault(entry.BucketId);
            var end = start + result.Buckets[i].Allocated;
            CushionFigures? cushion = null;
            LimitFigures? limit = null;
            if (entry.TargetMonths is { } months)
            {
                var target = months * figures.Cost;
                balances[entry.BucketId] = end;
                cushion = new CushionFigures(start, target, months, end, target - end < AllocationCalculator.CushionTolerance);
            }
            else if (kinds[entry.BucketId] is var kind && kind.IsLimited())
            {
                var yearLimit = limits.For(kind);
                balances[entry.BucketId] = end;
                limit = new LimitFigures(month.Year, yearLimit, start, end, yearLimit - end < AllocationCalculator.LimitTolerance, limits.Estimated);
            }

            buckets.Add(new LedgerBucket(entry, result.Buckets[i], cushion, limit));
        }

        return new LedgerMonth(inForce, buckets, result.Deficit);
    }
}
