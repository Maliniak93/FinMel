namespace Skarbiec.Portfolio.Features.Planning.Allocation;

public sealed record BucketInput(BucketShareType ShareType, decimal Share, bool IsRemainder)
{
    public bool IsCushion { get; private init; }

    public decimal CushionBalance { get; private init; }

    public decimal CushionTarget { get; private init; }

    public static BucketInput Cushion(decimal balanceStart, decimal target) =>
        new(BucketShareType.Fixed, 0m, false) { IsCushion = true, CushionBalance = balanceStart, CushionTarget = target };
}

public sealed record BucketAllocation(decimal Allocated, bool Underfunded);

public sealed record AllocationResult(IReadOnlyList<BucketAllocation> Buckets, decimal Deficit);

public static class AllocationCalculator
{
    // A gap under this counts as reached.
    public const decimal CushionTolerance = 1m;

    public static AllocationResult Allocate(decimal forecast, IReadOnlyList<BucketInput> buckets)
    {
        if (forecast <= 0m)
        {
            return new AllocationResult(buckets.Select(_ => new BucketAllocation(0m, false)).ToList(), -forecast);
        }

        var exact = new decimal[buckets.Count];
        var available = forecast;
        for (var i = 0; i < buckets.Count; i++)
        {
            if (!buckets[i].IsCushion)
            {
                continue;
            }

            var gap = buckets[i].CushionTarget - buckets[i].CushionBalance;
            exact[i] = gap < CushionTolerance ? 0m : Math.Floor(Math.Min(available, gap));
            available -= exact[i];
        }

        for (var i = 0; i < buckets.Count; i++)
        {
            if (buckets[i].IsCushion || buckets[i].ShareType != BucketShareType.Fixed)
            {
                continue;
            }

            exact[i] = Math.Min(buckets[i].Share, available);
            available -= exact[i];
        }

        for (var i = 0; i < buckets.Count; i++)
        {
            if (!buckets[i].IsCushion && buckets[i].ShareType == BucketShareType.Percent)
            {
                exact[i] = available * buckets[i].Share / 100m;
            }
        }

        var allocated = new decimal[buckets.Count];
        var remainderIndex = -1;
        var floored = 0m;
        for (var i = 0; i < buckets.Count; i++)
        {
            if (buckets[i].IsRemainder)
            {
                remainderIndex = i;
                continue;
            }

            allocated[i] = Math.Floor(exact[i]);
            floored += allocated[i];
        }

        if (remainderIndex >= 0)
        {
            allocated[remainderIndex] = forecast - floored;
        }

        var result = new BucketAllocation[buckets.Count];
        for (var i = 0; i < buckets.Count; i++)
        {
            var funded = buckets[i].IsRemainder ? allocated[i] : exact[i];
            var underfunded = !buckets[i].IsCushion && buckets[i].ShareType == BucketShareType.Fixed && funded < buckets[i].Share;
            result[i] = new BucketAllocation(allocated[i], underfunded);
        }

        return new AllocationResult(result, 0m);
    }
}
