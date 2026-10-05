using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondRedemptionMathTests
{
    private static decimal[] Rates(params double[] rates) => [.. rates.Select(r => (decimal)r)];

    private static BondEarlyPerBond Early(
        TreasuryBondType type, DateOnly purchaseDate, decimal[] rates, int daysIn, decimal fee)
    {
        var period = BondSchedule.Periods(type, purchaseDate)[rates.Length - 1];

        return BondRedemptionMath.EarlyPerBond(type, rates, period.Start, period.End, period.Start.AddDays(daysIn), fee);
    }

    [Fact]
    public void Early_PerTypeInterestAndFee()
    {
        var edo = Early(TreasuryBondType.Edo, new DateOnly(2025, 3, 1), Rates(5.35, 4.00), 73, 3.00m);
        Assert.Equal((6.19m, 3.00m), (edo.Interest, edo.Fee));

        var rorMonthOne = Early(TreasuryBondType.Ror, new DateOnly(2026, 6, 10), Rates(3.75), 15, 0.50m);
        Assert.Equal((0.16m, 0.16m), (rorMonthOne.Interest, rorMonthOne.Fee));

        var rorMonthFour = Early(TreasuryBondType.Ror, new DateOnly(2026, 6, 10), Rates(3.75, 3.75, 3.75, 3.75), 15, 0.50m);
        Assert.Equal((0.16m, 0.50m), (rorMonthFour.Interest, rorMonthFour.Fee));

        var ots = Early(TreasuryBondType.Ots, new DateOnly(2026, 6, 1), Rates(2.00), 46, 1.00m);
        Assert.Equal((0.00m, 0.00m), (ots.Interest, ots.Fee));

        var tos = Early(TreasuryBondType.Tos, new DateOnly(2025, 3, 1), Rates(4.40, 4.40), 73, 1.00m);
        Assert.Equal((5.32m, 1.00m), (tos.Interest, tos.Fee));

        var coi = Early(TreasuryBondType.Coi, new DateOnly(2025, 3, 1), Rates(5.35, 5.00), 183, 2.00m);
        Assert.Equal((2.51m, 2.00m), (coi.Interest, coi.Fee));
    }
}
