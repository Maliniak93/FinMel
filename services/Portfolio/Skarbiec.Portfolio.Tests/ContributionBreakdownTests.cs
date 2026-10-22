using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Split;

namespace Skarbiec.Portfolio.Tests;

public sealed class ContributionBreakdownTests
{
    private static readonly AssetGroupClassShare[] Allocation =
    [
        new(AssetClass.Etf, 50m),
        new(AssetClass.Bond, 40m),
        new(AssetClass.Cash, 10m),
    ];

    private static Dictionary<AssetClass, decimal> Parts(IReadOnlyList<ClassAmount> parts) =>
        parts.ToDictionary(p => p.AssetClass, p => p.AmountPln);

    [Fact]
    public void CorrectsTowardsTargetInProportionToGaps()
    {
        var values = new Dictionary<AssetClass, decimal>
        {
            [AssetClass.Etf] = 90_000m,
            [AssetClass.Bond] = 60_000m,
            [AssetClass.Cash] = 10_000m,
        };

        var parts = ContributionBreakdown.Compute(2_000m, Allocation, values);

        Assert.Equal(
            new Dictionary<AssetClass, decimal> { [AssetClass.Etf] = 0m, [AssetClass.Bond] = 800m, [AssetClass.Cash] = 1_200m },
            Parts(parts));
    }

    [Fact]
    public void FallsBackToPercentages()
    {
        var expected = new Dictionary<AssetClass, decimal>
        {
            [AssetClass.Etf] = 1_000m,
            [AssetClass.Bond] = 800m,
            [AssetClass.Cash] = 200m,
        };
        var onTarget = new Dictionary<AssetClass, decimal>
        {
            [AssetClass.Etf] = 50_000m,
            [AssetClass.Bond] = 40_000m,
            [AssetClass.Cash] = 10_000m,
        };

        var noAssets = ContributionBreakdown.Compute(2_000m, Allocation, new Dictionary<AssetClass, decimal>());
        var noPositiveGap = ContributionBreakdown.Compute(2_000m, Allocation, onTarget);

        Assert.Equal(expected, Parts(noAssets));
        Assert.Equal(expected, Parts(noPositiveGap));
    }

    [Fact]
    public void PartsAreRoundedToCentsAndSumToTheContribution()
    {
        var thirds = new AssetGroupClassShare[]
        {
            new(AssetClass.Etf, 33.3m),
            new(AssetClass.Bond, 33.3m),
            new(AssetClass.Cash, 33.4m),
        };

        var parts = ContributionBreakdown.Compute(100m, thirds, new Dictionary<AssetClass, decimal>());

        Assert.Equal(100m, parts.Sum(p => p.AmountPln));
        Assert.All(parts, p => Assert.Equal(decimal.Round(p.AmountPln, 2), p.AmountPln));
        Assert.All(parts, p => Assert.True(p.AmountPln >= 0m));
    }
}
