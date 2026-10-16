namespace Skarbiec.Portfolio.Features.Planning.Allocation;

public sealed record BucketInput(BucketShareType ShareType, decimal Share, bool IsRemainder);

public sealed record BucketAllocation(decimal Allocated, bool Underfunded);

public sealed record AllocationResult(IReadOnlyList<BucketAllocation> Buckets, decimal Deficit);

public static class AllocationCalculator
{
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
            if (buckets[i].ShareType != BucketShareType.Fixed)
            {
                continue;
            }

            exact[i] = Math.Min(buckets[i].Share, available);
            available -= exact[i];
        }

        for (var i = 0; i < buckets.Count; i++)
        {
            if (buckets[i].ShareType == BucketShareType.Percent)
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
            var underfunded = buckets[i].ShareType == BucketShareType.Fixed && funded < buckets[i].Share;
            result[i] = new BucketAllocation(allocated[i], underfunded);
        }

        return new AllocationResult(result, 0m);
    }
}
