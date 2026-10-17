using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.TargetAllocation;

namespace Skarbiec.Reporting.Tests;

public sealed class TargetAllocationMathTests
{
    [Fact]
    public void Band_InclusiveAndClipped()
    {
        var groups = new List<TargetAllocationGroup>
        {
            new("A", [AssetClass.Stock], 40m, 5m),
            new("B", [AssetClass.Etf], 0m, 5m),
            new("C", [AssetClass.Bond], 60m, 5m),
        };
        var values = new Dictionary<AssetClass, decimal>
        {
            [AssetClass.Stock] = 45m,
            [AssetClass.Etf] = 6m,
            [AssetClass.Bond] = 49m,
        };

        var result = TargetAllocationMath.Compute(groups, values);

        Assert.Equal(45.0m, result.Groups[0].CurrentPercent);
        Assert.True(result.Groups[0].IsInBand);
        Assert.Equal(6.0m, result.Groups[1].CurrentPercent);
        Assert.False(result.Groups[1].IsInBand);
        Assert.Equal(0m, result.Groups[1].BandLowPercent);
        Assert.Equal(5m, result.Groups[1].BandHighPercent);

        var full = TargetAllocationMath.Compute(
            [new TargetAllocationGroup("All", [AssetClass.Stock], 100m, 5m)],
            new Dictionary<AssetClass, decimal> { [AssetClass.Stock] = 10m });

        Assert.Equal(95m, full.Groups[0].BandLowPercent);
        Assert.Equal(100m, full.Groups[0].BandHighPercent);
    }

    [Fact]
    public void GroupWithoutAssets_FullShortfall()
    {
        var groups = new List<TargetAllocationGroup>
        {
            new("Gotowka", [AssetClass.Cash], 10m, 5m),
            new("Akcje", [AssetClass.Stock], 90m, 5m),
        };
        var values = new Dictionary<AssetClass, decimal> { [AssetClass.Stock] = 180_000m };

        var result = TargetAllocationMath.Compute(groups, values);

        Assert.Equal(180_000m, result.BasePln);
        var empty = result.Groups[0];
        Assert.Equal(0m, empty.ValuePln);
        Assert.Equal(0.0m, empty.CurrentPercent);
        Assert.Equal(-10.0m, empty.DeviationPp);
        Assert.False(empty.IsInBand);
        Assert.Equal(18_000m, empty.TargetValuePln);
        Assert.Equal(-18_000m, empty.DeviationPln);
    }
}
