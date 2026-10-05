using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Bonds;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondRedemptionMathPropertyTests
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
    public void Early_AnyTypeAndDate_KeepsFeeTaxAndProceedsConsistent(int seed)
    {
        var random = new Random(seed);
        var type = AllTypes[random.Next(AllTypes.Length)];
        var periods = BondSchedule.Periods(type, PurchaseDate);
        var period = periods[random.Next(periods.Count)];
        var rates = Enumerable.Range(0, period.Index).Select(_ => random.Next(0, 2_001) / 100m).ToArray();
        var date = period.Start.AddDays(random.Next(0, period.End.DayNumber - period.Start.DayNumber));
        var fee = random.Next(0, 1_001) / 100m;
        var price = 100m - random.Next(0, 1_001) / 100m;
        var bondCount = random.Next(1, 500);
        var taxExempt = random.Next(2) == 0;

        var perBond = BondRedemptionMath.EarlyPerBond(type, rates, period.Start, period.End, date, fee);
        var amounts = BondRedemptionMath.Early(type, rates, period.Start, period.End, date, fee, price, bondCount, taxExempt);

        var capitalising = !BondSchedule.IsCoupon(type);
        if (type is TreasuryBondType.Ots)
        {
            Assert.Equal(0m, perBond.Interest);
            Assert.Equal(0m, perBond.Fee);
        }
        else if (capitalising || period.Index == 1)
        {
            Assert.True(perBond.Fee <= perBond.Interest);
            Assert.True(amounts.Proceeds >= bondCount * price);
        }
        else
        {
            Assert.Equal(fee, perBond.Fee);
        }

        var discount = bondCount * (100m - price);
        var taxable = Math.Max(0m, bondCount * (perBond.Interest - perBond.Fee) + discount);
        Assert.Equal(BelkaTax.On(taxable, taxExempt), amounts.Tax);
        Assert.True(amounts.Tax >= 0m);
        Assert.Equal(bondCount * (100m + perBond.Interest - perBond.Fee) - amounts.Tax, amounts.Proceeds);
    }
}
