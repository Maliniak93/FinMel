using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Bonds;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondInterestMathPropertyTests
{
    private const int CaseCount = 200;

    private static readonly TreasuryBondType[] CapitalisingTypes =
        [TreasuryBondType.Ots, TreasuryBondType.Tos, TreasuryBondType.Edo, TreasuryBondType.Ros, TreasuryBondType.Rod];

    private static readonly TreasuryBondType[] CouponTypes =
        [TreasuryBondType.Ror, TreasuryBondType.Dor, TreasuryBondType.Coi];

    private static readonly DateOnly PurchaseDate = new(2020, 3, 31);

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    private static decimal[] RandomRates(Random random, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => random.Next(0, 2_001) / 100m)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void PerBond_CapitalisingType_SumsToTheCompoundedGrowthAndEachPeriodIsNonNegative(int seed)
    {
        var random = new Random(seed);
        var type = CapitalisingTypes[random.Next(CapitalisingTypes.Length)];
        var periods = BondSchedule.Periods(type, PurchaseDate);
        var rates = RandomRates(random, periods.Count);

        var sum = 0m;
        for (var k = 1; k <= periods.Count; k++)
        {
            var perBond = BondInterestMath.PerBond(type, rates[..k], periods[k - 1].Start, periods[k - 1].End);
            Assert.True(perBond >= 0m);
            sum += perBond;
        }

        if (type is TreasuryBondType.Ots)
        {
            return;
        }

        var product = rates.Aggregate(1m, (acc, r) => acc * (1m + r / 100m));
        var capital = Math.Round(100m * product, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(capital - 100m, sum);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Settle_CouponType_NetIsGrossMinusBelkaTax(int seed)
    {
        var random = new Random(seed);
        var type = CouponTypes[random.Next(CouponTypes.Length)];
        var period = BondSchedule.Periods(type, PurchaseDate)[0];
        var rates = RandomRates(random, 1);
        var bondCount = random.Next(1, 500);
        var taxExempt = random.Next(2) == 0;

        var amounts = BondInterestMath.Settle(type, rates, period.Start, period.End, bondCount, taxExempt);

        Assert.Equal(BondInterestMath.PerBond(type, rates, period.Start, period.End) * bondCount, amounts.Gross);
        Assert.Equal(BelkaTax.On(amounts.Gross, taxExempt), amounts.Tax);
        Assert.Equal(amounts.Gross - amounts.Tax, amounts.Net);
    }
}
