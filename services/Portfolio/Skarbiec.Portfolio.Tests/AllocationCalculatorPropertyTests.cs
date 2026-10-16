using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;

namespace Skarbiec.Portfolio.Tests;

public sealed class AllocationCalculatorPropertyTests
{
    private const int CaseCount = 300;

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
