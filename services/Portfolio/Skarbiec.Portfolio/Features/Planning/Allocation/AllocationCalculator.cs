namespace Skarbiec.Portfolio.Features.Planning.Allocation;

public sealed record BucketInput(BucketShareType ShareType, decimal Share, bool IsRemainder)
{
    public bool IsCushion { get; private init; }

    public decimal CushionBalance { get; private init; }

    public decimal CushionTarget { get; private init; }

    public bool IsLimited { get; private init; }

    public decimal Room { get; private init; }

    public static BucketInput Cushion(decimal balanceStart, decimal target) =>
        new(BucketShareType.Fixed, 0m, false) { IsCushion = true, CushionBalance = balanceStart, CushionTarget = target };

    public static BucketInput Limited(BucketShareType shareType, decimal share, bool isRemainder, decimal room) =>
        new(shareType, share, isRemainder) { IsLimited = true, Room = room };
}

public sealed record BucketAllocation(decimal Allocated, bool Underfunded, bool LimitReached = false);

public sealed record AllocationResult(IReadOnlyList<BucketAllocation> Buckets, decimal Deficit);

public static class AllocationCalculator
{
    // A gap under this counts as reached.
    public const decimal CushionTolerance = 1m;

    // A room under this counts as used up.
    public const decimal LimitTolerance = 1m;

    public static AllocationResult Allocate(decimal forecast, IReadOnlyList<BucketInput> buckets)
    {
        if (forecast <= 0m)
        {
            return new AllocationResult(buckets.Select(_ => new BucketAllocation(0m, false)).ToList(), -forecast);
        }

        var exact = new decimal[buckets.Count];
        var limitReached = new bool[buckets.Count];
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

            // What the room refuses stays out of the percent base, so it ends in the remainder.
            var wanted = Math.Min(buckets[i].Share, available);
            exact[i] = IsCapped(buckets[i]) ? Math.Min(wanted, RoomOf(buckets[i])) : wanted;
            limitReached[i] = exact[i] < wanted;
            available -= wanted;
        }

        var carry = 0m;
        for (var i = 0; i < buckets.Count; i++)
        {
            if (buckets[i].IsCushion || buckets[i].ShareType != BucketShareType.Percent)
            {
                continue;
            }

            var own = available * buckets[i].Share / 100m;
            if (buckets[i].IsRemainder)
            {
                exact[i] = own;
                continue;
            }

            var wanted = own + carry;
            exact[i] = IsCapped(buckets[i]) ? Math.Min(wanted, RoomOf(buckets[i])) : wanted;
            carry = wanted - exact[i];
            limitReached[i] = carry > 0m;
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
            var underfunded = !buckets[i].IsCushion
                && buckets[i].ShareType == BucketShareType.Fixed
                && !limitReached[i]
                && funded < buckets[i].Share;
            result[i] = new BucketAllocation(allocated[i], underfunded, limitReached[i]);
        }

        return new AllocationResult(result, 0m);
    }

    private static bool IsCapped(BucketInput bucket) => bucket is { IsLimited: true, IsRemainder: false };

    private static decimal RoomOf(BucketInput bucket) => bucket.Room < LimitTolerance ? 0m : bucket.Room;
}
