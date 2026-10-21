using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;

namespace Skarbiec.Portfolio.Tests;

public sealed class AllocationCalculatorPropertyTests
{
    private const int CaseCount = 50;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    private static List<BucketInput> RandomPlan(Random random)
    {
        var count = random.Next(1, 9);
        var remainderIndex = random.Next(count);
        var percentLeft = 10_000;
        var plan = new List<BucketInput>();
        for (var i = 0; i < count; i++)
        {
            var isRemainder = i == remainderIndex;
            if (random.Next(2) == 0)
            {
                var share = isRemainder ? random.Next(1, 100_000) / 100m : random.Next(1, 600_000) / 100m;
                plan.Add(new BucketInput(BucketShareType.Fixed, share, isRemainder));
                continue;
            }

            var hundredths = Math.Min(percentLeft, random.Next(isRemainder ? 0 : 1, 4_000));
            percentLeft -= hundredths;
            plan.Add(new BucketInput(BucketShareType.Percent, hundredths / 100m, isRemainder));
        }

        return plan;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Allocate_RandomPlanWithCushion_NeverExceedsTheGapOrTheForecast(int seed)
    {
        var random = new Random(seed);
        var forecast = random.Next(-200_000, 3_000_000) / 100m;
        var cost = random.Next(0, 800_000) / 100m;
        var target = random.Next(1, 25) * cost;
        var balance = random.Next(0, 3_000_000) / 100m;
        var plan = new List<BucketInput> { BucketInput.Cushion(balance, target) };
        plan.AddRange(RandomPlan(random));

        var result = AllocationCalculator.Allocate(forecast, plan);

        var cushion = result.Buckets[0].Allocated;
        var gap = Math.Max(target - balance, 0m);
        Assert.True(cushion >= 0m);
        Assert.Equal(Math.Floor(cushion), cushion);
        Assert.True(cushion <= Math.Max(forecast, 0m));
        Assert.True(cushion <= gap);
        if (forecast > 0m && gap >= 1m)
        {
            Assert.Equal(Math.Floor(Math.Min(forecast, gap)), cushion);
        }

        if (gap < 1m)
        {
            Assert.Equal(0m, cushion);
        }

        Assert.Equal(Math.Max(forecast, 0m), result.Buckets.Sum(b => b.Allocated));
    }

    private static List<BucketInput> RandomLimitedPlan(Random random, out List<decimal?> rooms)
    {
        var plain = RandomPlan(random);
        rooms = [];
        var plan = new List<BucketInput>();
        foreach (var bucket in plain)
        {
            if (bucket.IsRemainder || random.Next(2) == 0)
            {
                rooms.Add(null);
                plan.Add(bucket);
                continue;
            }

            var room = random.Next(0, 1_500_000) / 100m;
            rooms.Add(room);
            plan.Add(BucketInput.Limited(bucket.ShareType, bucket.Share, bucket.IsRemainder, room));
        }

        return plan;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Allocate_RandomPlanWithLimits_NeverExceedsARoomAndKeepsTheSum(int seed)
    {
        var random = new Random(seed);
        var forecast = random.Next(-200_000, 3_000_000) / 100m;
        var plan = RandomLimitedPlan(random, out var rooms);

        var result = AllocationCalculator.Allocate(forecast, plan);

        Assert.Equal(Math.Max(forecast, 0m), result.Buckets.Sum(b => b.Allocated));
        for (var i = 0; i < plan.Count; i++)
        {
            Assert.True(result.Buckets[i].Allocated >= 0m);
            if (rooms[i] is { } room)
            {
                Assert.True(result.Buckets[i].Allocated <= room, $"bucket {i}: {result.Buckets[i].Allocated} > {room}");
                if (room < 1m)
                {
                    Assert.Equal(0m, result.Buckets[i].Allocated);
                }
            }

            if (!plan[i].IsRemainder)
            {
                Assert.Equal(Math.Floor(result.Buckets[i].Allocated), result.Buckets[i].Allocated);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Allocate_PercentLimitedBucketsOverflow_LandBelowWithRoomOrInTheRemainder(int seed)
    {
        var random = new Random(seed);
        var forecast = random.Next(100_000, 3_000_000) / 100m;
        var room = random.Next(0, 200_000) / 100m;
        var plan = new List<BucketInput>
        {
            BucketInput.Limited(BucketShareType.Percent, 50m, false, room),
            new(BucketShareType.Percent, 30m, false),
            new(BucketShareType.Percent, 20m, true)
        };

        var result = AllocationCalculator.Allocate(forecast, plan);

        var first = result.Buckets[0].Allocated;
        var overflow = Math.Max(forecast * 0.5m - (room < 1m ? 0m : room), 0m);
        Assert.True(first <= room);
        Assert.True(result.Buckets[1].Allocated <= Math.Floor(forecast * 0.3m + overflow));
        Assert.True(result.Buckets[1].Allocated >= Math.Floor(forecast * 0.3m));
        Assert.Equal(forecast, result.Buckets.Sum(b => b.Allocated));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Allocate_RandomPlan_KeepsTheAllocationInvariants(int seed)
    {
        var random = new Random(seed);
        var forecast = random.Next(-200_000, 3_000_000) / 100m;
        var plan = RandomPlan(random);

        var result = AllocationCalculator.Allocate(forecast, plan);

        Assert.Equal(plan.Count, result.Buckets.Count);
        Assert.Equal(Math.Max(forecast, 0m), result.Buckets.Sum(b => b.Allocated));
        Assert.Equal(Math.Max(-forecast, 0m), result.Deficit);

        var fixedLeft = Math.Max(forecast, 0m);
        for (var i = 0; i < plan.Count; i++)
        {
            var allocated = result.Buckets[i].Allocated;
            Assert.True(allocated >= 0m);
            if (plan[i].IsRemainder)
            {
                continue;
            }

            Assert.Equal(Math.Floor(allocated), allocated);
            if (plan[i].ShareType == BucketShareType.Fixed)
            {
                Assert.True(allocated <= plan[i].Share);
                fixedLeft -= allocated;
            }
        }

        for (var i = 0; i < plan.Count; i++)
        {
            if (!plan[i].IsRemainder && plan[i].ShareType == BucketShareType.Percent)
            {
                Assert.True(result.Buckets[i].Allocated <= fixedLeft * plan[i].Share / 100m);
            }
        }
    }
}
