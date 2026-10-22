using Skarbiec.Portfolio.Features.Planning.Split;

namespace Skarbiec.Portfolio.Tests;

public sealed class GroupSplitCalculatorPropertyTests
{
    private const int CaseCount = 200;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    private static GroupShareInput[] RandomShares(Random random)
    {
        var count = random.Next(0, 7);
        var tenths = new int[count];
        for (var left = count == 0 ? 0 : 1_000; left > 0; left--)
        {
            tenths[random.Next(count)]++;
        }

        return [.. tenths.Select((t, i) => new GroupShareInput(Guid.NewGuid(), $"Grupa {i}", t / 10m))];
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void AmountsAreWholeNeverNegativeAndSumToTheFreeFunds(int seed)
    {
        var random = new Random(seed);
        var freeFunds = random.Next(1, 200_001);
        var missing = random.Next(3) == 0 ? 0m : random.Next(0, 250_001) + random.Next(100) / 100m;
        var shares = RandomShares(random);

        var split = GroupSplitCalculator.Split(freeFunds, missing, shares);

        var amounts = split.Groups.Select(g => g.AllocationPln).Append(split.CushionPln).Append(split.UnassignedPln).ToList();
        Assert.All(amounts, a => Assert.Equal(decimal.Truncate(a), a));
        Assert.All(amounts, a => Assert.True(a >= 0m));
        Assert.Equal(freeFunds, amounts.Sum());
        Assert.Equal(Math.Floor(Math.Min(freeFunds, missing)), split.CushionPln);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FreeFundsWithGroszeStillGiveAWholeCushionAndSumToTheFreeFunds(int seed)
    {
        var random = new Random(seed);
        var freeFunds = random.Next(1, 200_001) + random.Next(100) / 100m;
        var missing = random.Next(0, 250_001) + random.Next(100) / 100m;

        var split = GroupSplitCalculator.Split(freeFunds, missing, RandomShares(random));

        var amounts = split.Groups.Select(g => g.AllocationPln).Append(split.CushionPln).Append(split.UnassignedPln).ToList();
        Assert.Equal(decimal.Truncate(split.CushionPln), split.CushionPln);
        Assert.All(amounts, a => Assert.True(a >= 0m));
        Assert.Equal(freeFunds, amounts.Sum());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void DeficitNeverGivesAnAmount(int seed)
    {
        var random = new Random(seed);
        var freeFunds = -random.Next(0, 100_001);

        var split = GroupSplitCalculator.Split(freeFunds, random.Next(0, 50_000), RandomShares(random));

        Assert.Equal(0m, split.CushionPln);
        Assert.Equal(0m, split.UnassignedPln);
        Assert.All(split.Groups, g => Assert.Equal(0m, g.AllocationPln));
    }
}
