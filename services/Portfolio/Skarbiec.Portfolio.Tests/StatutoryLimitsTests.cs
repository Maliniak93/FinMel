using Skarbiec.Portfolio.Features.Planning.Allocation;

namespace Skarbiec.Portfolio.Tests;

public sealed class StatutoryLimitsTests
{
    [Theory]
    [InlineData(2025, 26_019, 10_407.60, 15_611.40, false)]
    [InlineData(2026, 28_260, 11_304, 16_956, false)]
    [InlineData(2024, 23_472, 9_388.80, 14_083.20, false)]
    [InlineData(2031, 28_260, 11_304, 16_956, true)]
    [InlineData(2023, 23_472, 9_388.80, 14_083.20, true)]
    public void For_Year_ReturnsTheKnownOrEstimatedLimits(int year, double ike, double ikze, double ikzeJdg, bool estimated)
    {
        var limits = StatutoryLimits.For(year);

        Assert.Equal((decimal)ike, limits.Ike);
        Assert.Equal((decimal)ikze, limits.Ikze);
        Assert.Equal((decimal)ikzeJdg, limits.IkzeJdg);
        Assert.Equal(estimated, limits.Estimated);
    }
}
