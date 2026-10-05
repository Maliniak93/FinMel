using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondValuationPropertyTests
{
    private const int CaseCount = 200;

    private static readonly TreasuryBondType[] AllTypes = Enum.GetValues<TreasuryBondType>();

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

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Today_AnyTypeRatesAndDate_KeepsGrossAboveNominalAndNetBelowGross(int seed)
    {
        var random = new Random(seed);
        var type = AllTypes[random.Next(AllTypes.Length)];
        var periods = BondSchedule.Periods(type, PurchaseDate);
        var period = periods[random.Next(periods.Count)];
        var today = period.Start.AddDays(random.Next(0, period.End.DayNumber - period.Start.DayNumber));
        var bondCount = random.Next(1, 500);
        var bond = BondValuationTests.NewBond(
            type,
            PurchaseDate,
            bondCount,
            firstPeriodRate: random.Next(0, 2_001) / 100m,
            fee: random.Next(0, 1_001) / 100m,
            price: 100m - random.Next(0, 1_001) / 100m,
            taxExempt: random.Next(2) == 0);

        var catalog = new Dictionary<int, decimal>();
        var rates = new List<decimal>();
        var settlements = new List<Skarbiec.Portfolio.Data.BondInterestSettlement>();
        foreach (var p in periods.Where(p => p.Index <= period.Index))
        {
            var rate = BondSchedule.IsFixedRate(type) || p.Index == 1 ? bond.FirstPeriodRatePercent : random.Next(0, 2_001) / 100m;
            rates.Add(rate);
            if (p.Index < period.Index)
            {
                var gross = BondInterestMath.Settle(type, rates, p.Start, p.End, bondCount, bond.TaxExempt).Gross;
                settlements.Add(BondValuationTests.NewSettlement(bond, p.Index, rate, gross));
            }
            else
            {
                catalog[p.Index] = rate;
            }
        }

        var outcome = BondValuation.Today(bond, settlements, catalog, today);

        var estimate = Assert.IsType<BondEstimateResponse>(outcome.Estimate);
        if (!BondSchedule.IsCoupon(type))
        {
            Assert.True(estimate.GrossValue >= bondCount * 100m);
        }

        Assert.True(estimate.NetValue <= estimate.GrossValue);
    }
}
