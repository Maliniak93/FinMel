using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondInterestMathTests
{
    private static decimal[] Rates(params double[] rates) => [.. rates.Select(r => (decimal)r)];

    private static decimal TotalPerBond(TreasuryBondType type, DateOnly purchaseDate, decimal[] rates)
    {
        var periods = BondSchedule.Periods(type, purchaseDate);
        var total = 0m;
        for (var k = 1; k <= rates.Length; k++)
        {
            total += BondInterestMath.PerBond(type, rates[..k], periods[k - 1].Start, periods[k - 1].End);
        }

        return total;
    }

    [Fact]
    public void PerBond_Edo1014PublishedRates_SumsTo84_08()
    {
        var rates = Rates(7.10, 5.10, 5.10, 5.00, 8.30, 7.20, 5.50, 7.80, 7.30, 4.60);

        Assert.Equal(84.08m, TotalPerBond(TreasuryBondType.Edo, new DateOnly(2014, 10, 1), rates));
    }

    [Fact]
    public void PerBond_Edo0116PublishedRates_SumsTo59_90()
    {
        var rates = Rates(5.00, 3.65, 5.85, 5.95, 5.55, 4.95, 7.05, 5.05, 2.85, 2.25);

        Assert.Equal(59.90m, TotalPerBond(TreasuryBondType.Edo, new DateOnly(2006, 1, 1), rates));
    }

    [Fact]
    public void PerBond_Ros1022PublishedRates_SumsTo29_47()
    {
        var rates = Rates(2.60, 3.55, 3.75, 4.65, 4.65, 7.25);

        Assert.Equal(29.47m, TotalPerBond(TreasuryBondType.Ros, new DateOnly(2022, 10, 1), rates));
    }

    [Fact]
    public void PerBond_TosAt4_40_PaysFixedCompoundedYears()
    {
        var rates = Rates(4.40, 4.40, 4.40);
        var purchase = new DateOnly(2026, 10, 1);
        var periods = BondSchedule.Periods(TreasuryBondType.Tos, purchase);

        var perYear = Enumerable.Range(1, 3)
            .Select(k => BondInterestMath.PerBond(TreasuryBondType.Tos, rates[..k], periods[k - 1].Start, periods[k - 1].End))
            .ToArray();

        Assert.Equal([4.40m, 4.59m, 4.80m], perYear);
        Assert.Equal(13.79m, perYear.Sum());
    }

    [Theory]
    [InlineData(2.00, 2026, 10, 1, 0.50)]
    [InlineData(2.50, 2026, 2, 1, 0.61)]
    public void PerBond_Ots_UsesTheActualDayCountOverA365Year(double rate, int year, int month, int day, double expected)
    {
        var purchase = new DateOnly(year, month, day);
        var period = Assert.Single(BondSchedule.Periods(TreasuryBondType.Ots, purchase));

        var perBond = BondInterestMath.PerBond(TreasuryBondType.Ots, [(decimal)rate], period.Start, period.End);

        Assert.Equal((decimal)expected, perBond);
    }

    [Theory]
    [InlineData(5.25, 0.44)]
    [InlineData(6.00, 0.50)]
    [InlineData(6.50, 0.54)]
    public void PerBond_Ror_IsAMonthlyTwelfthOfTheRate(double rate, double expected)
    {
        var period = BondSchedule.Periods(TreasuryBondType.Ror, new DateOnly(2026, 6, 10))[0];

        Assert.Equal((decimal)expected, BondInterestMath.PerBond(TreasuryBondType.Ror, [(decimal)rate], period.Start, period.End));
    }

    [Fact]
    public void PerBond_DorAt4_15_IsAMonthlyTwelfthOfTheRate()
    {
        var period = BondSchedule.Periods(TreasuryBondType.Dor, new DateOnly(2026, 6, 10))[0];

        Assert.Equal(0.35m, BondInterestMath.PerBond(TreasuryBondType.Dor, [4.15m], period.Start, period.End));
    }

    [Fact]
    public void PerBond_CoiAt6_55_PaysTheWholeYearlyRate()
    {
        var period = BondSchedule.Periods(TreasuryBondType.Coi, new DateOnly(2026, 6, 10))[0];

        Assert.Equal(6.55m, BondInterestMath.PerBond(TreasuryBondType.Coi, [6.55m], period.Start, period.End));
    }
}
