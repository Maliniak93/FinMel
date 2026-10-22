using Skarbiec.Portfolio.Features.Planning.Split;

namespace Skarbiec.Portfolio.Tests;

public sealed class GroupSplitCalculatorTests
{
    private static readonly Guid Current = Guid.NewGuid();
    private static readonly Guid Retirement = Guid.NewGuid();
    private static readonly Guid Investing = Guid.NewGuid();
    private static readonly Guid Holidays = Guid.NewGuid();

    private static readonly GroupShareInput[] Shares =
    [
        new(Current, "Bieżące", 30m),
        new(Retirement, "Emerytura", 10m),
        new(Investing, "Inwestycje", 10m),
        new(Holidays, "Wakacje", 50m),
    ];

    private static decimal Of(GroupSplit split, Guid groupId) => split.Groups.Single(g => g.GroupId == groupId).AllocationPln;

    [Fact]
    public void CushionTakesOnlyTheMissingAmount()
    {
        var split = GroupSplitCalculator.Split(4_000m, 1_500m, Shares);

        Assert.Equal(1_500m, split.CushionPln);
        Assert.Equal(750m, Of(split, Current));
        Assert.Equal(250m, Of(split, Retirement));
        Assert.Equal(250m, Of(split, Investing));
        Assert.Equal(1_250m, Of(split, Holidays));
        Assert.Equal(0m, split.UnassignedPln);
        Assert.Equal(0m, split.DeficitPln);
    }

    [Fact]
    public void FullOrMissingCushionSplitsEverythingByShares()
    {
        var full = GroupSplitCalculator.Split(4_000m, 0m, Shares);

        Assert.Equal(0m, full.CushionPln);
        Assert.Equal(1_200m, Of(full, Current));
        Assert.Equal(400m, Of(full, Retirement));
        Assert.Equal(400m, Of(full, Investing));
        Assert.Equal(2_000m, Of(full, Holidays));
    }

    [Fact]
    public void DeficitGivesNoAmounts()
    {
        var negative = GroupSplitCalculator.Split(-500m, 1_500m, Shares);
        var zero = GroupSplitCalculator.Split(0m, 1_500m, Shares);

        Assert.Equal(500m, negative.DeficitPln);
        Assert.Equal(0m, negative.CushionPln);
        Assert.All(negative.Groups, g => Assert.Equal(0m, g.AllocationPln));
        Assert.Equal(0m, negative.UnassignedPln);
        Assert.Equal(0m, zero.CushionPln);
        Assert.All(zero.Groups, g => Assert.Equal(0m, g.AllocationPln));
    }

    [Fact]
    public void WithoutRegularGroupsWhatTheCushionLeavesIsUnassigned()
    {
        var split = GroupSplitCalculator.Split(4_000m, 1_500m, []);

        Assert.Equal(1_500m, split.CushionPln);
        Assert.Empty(split.Groups);
        Assert.Equal(2_500m, split.UnassignedPln);
    }

    [Fact]
    public void FlooredAmountsLeaveTheRemainderToTheLargestShare()
    {
        var shares = new GroupShareInput[] { new(Current, "Mała", 33.3m), new(Retirement, "Duża", 66.7m) };

        var split = GroupSplitCalculator.Split(100m, 0m, shares);

        Assert.Equal(33m, Of(split, Current));
        Assert.Equal(67m, Of(split, Retirement));
    }
}
