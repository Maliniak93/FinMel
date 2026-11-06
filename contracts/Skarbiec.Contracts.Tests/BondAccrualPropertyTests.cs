using Skarbiec.Contracts.Bonds;

namespace Skarbiec.Contracts.Tests;

public sealed class BondAccrualPropertyTests
{
    [Theory]
    [InlineData(TreasuryBondType.Ots, 3.00)]
    [InlineData(TreasuryBondType.Ror, 4.25)]
    [InlineData(TreasuryBondType.Dor, 4.60)]
    [InlineData(TreasuryBondType.Coi, 6.00)]
    [InlineData(TreasuryBondType.Tos, 4.40)]
    [InlineData(TreasuryBondType.Edo, 5.35)]
    public void MatchesPreviousEstimate_ForNominalPurchase(TreasuryBondType type, double rate)
    {
        var firstRate = (decimal)rate;
        var purchase = new DateOnly(2026, 1, 15);
        var terms = new BondTerms(type, purchase, BondCount: 7, FirstPeriodRatePercent: firstRate);
        var first = BondSchedule.Periods(type, purchase)[0];
        var catalog = new Dictionary<int, decimal> { [2] = firstRate, [3] = firstRate };

        var result = BondAccrual.Unsettled(terms, new Dictionary<int, decimal>(), catalog, first.End);

        Assert.Equal(7 * BondInterestMath.PerBond(type, [firstRate], first.Start, first.End), result);
    }
}
