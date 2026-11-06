using Skarbiec.Contracts.Bonds;

namespace Skarbiec.Contracts.Tests;

public sealed class BondAccrualTests
{
    [Fact]
    public void Capitalising_ReturnsCurrentPeriodOnly()
    {
        var terms = new BondTerms(TreasuryBondType.Edo, new DateOnly(2024, 3, 1), BondCount: 10, FirstPeriodRatePercent: 5.35m);
        var settled = new Dictionary<int, decimal> { [1] = 5.35m, [2] = 4.00m };
        var catalog = new Dictionary<int, decimal> { [3] = 3.00m };

        var result = BondAccrual.Unsettled(terms, settled, catalog, new DateOnly(2026, 5, 13));

        Assert.Equal(6.60m, result);
    }

    [Fact]
    public void Coupon_ReturnsEndedUnsettledAndRunning()
    {
        var terms = new BondTerms(TreasuryBondType.Coi, new DateOnly(2024, 3, 1), BondCount: 10, FirstPeriodRatePercent: 6.00m);
        var catalog = new Dictionary<int, decimal> { [2] = 5.00m };

        var result = BondAccrual.Unsettled(terms, new Dictionary<int, decimal>(), catalog, new DateOnly(2025, 5, 13));

        Assert.Equal(70.00m, result);
    }

    [Theory]
    [InlineData(2024, 3, 1)]
    [InlineData(2024, 2, 20)]
    public void PurchaseDay_ReturnsZero(int year, int month, int day)
    {
        var terms = new BondTerms(TreasuryBondType.Edo, new DateOnly(2024, 3, 1), BondCount: 10, FirstPeriodRatePercent: 5.35m);

        var result = BondAccrual.Unsettled(terms, new Dictionary<int, decimal>(), new Dictionary<int, decimal>(), new DateOnly(year, month, day));

        Assert.Equal(0m, result);
    }

    [Fact]
    public void MissingRate_IsUnavailable()
    {
        var terms = new BondTerms(TreasuryBondType.Edo, new DateOnly(2024, 3, 1), BondCount: 10, FirstPeriodRatePercent: 5.35m);
        var settled = new Dictionary<int, decimal> { [1] = 5.35m };

        var result = BondAccrual.Unsettled(terms, settled, new Dictionary<int, decimal>(), new DateOnly(2025, 5, 13));

        Assert.Null(result);
    }
}
