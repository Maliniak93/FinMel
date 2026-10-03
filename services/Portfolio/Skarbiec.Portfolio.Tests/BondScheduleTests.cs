using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondScheduleTests
{
    private static readonly DateOnly EndOfMonth = new(2026, 1, 31);

    [Theory]
    [InlineData(TreasuryBondType.Ots, 2026, 4, 30)]
    [InlineData(TreasuryBondType.Ror, 2027, 1, 31)]
    [InlineData(TreasuryBondType.Dor, 2028, 1, 31)]
    [InlineData(TreasuryBondType.Tos, 2029, 1, 31)]
    [InlineData(TreasuryBondType.Coi, 2030, 1, 31)]
    [InlineData(TreasuryBondType.Edo, 2036, 1, 31)]
    [InlineData(TreasuryBondType.Ros, 2032, 1, 31)]
    [InlineData(TreasuryBondType.Rod, 2038, 1, 31)]
    public void MaturityDate_EveryType_FollowsTheTypesTerm(TreasuryBondType type, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), BondSchedule.MaturityDate(type, EndOfMonth));
    }

    [Theory]
    [InlineData(TreasuryBondType.Ots, 1)]
    [InlineData(TreasuryBondType.Ror, 12)]
    [InlineData(TreasuryBondType.Dor, 24)]
    [InlineData(TreasuryBondType.Tos, 3)]
    [InlineData(TreasuryBondType.Coi, 4)]
    [InlineData(TreasuryBondType.Edo, 10)]
    [InlineData(TreasuryBondType.Ros, 6)]
    [InlineData(TreasuryBondType.Rod, 12)]
    public void Periods_EveryType_HasOnePeriodPerInterestStepEndingAtMaturity(TreasuryBondType type, int expectedCount)
    {
        var periods = BondSchedule.Periods(type, EndOfMonth);

        Assert.Equal(expectedCount, periods.Count);
        Assert.Equal(Enumerable.Range(1, expectedCount), periods.Select(p => p.Index));
        Assert.Equal(EndOfMonth, periods[0].Start);
        Assert.Equal(BondSchedule.MaturityDate(type, EndOfMonth), periods[^1].End);
        for (var i = 1; i < periods.Count; i++)
        {
            Assert.Equal(periods[i - 1].End, periods[i].Start);
        }
    }

    [Fact]
    public void Periods_Ror_EndsAtMonthEndsWithoutChaining()
    {
        var ends = BondSchedule.Periods(TreasuryBondType.Ror, EndOfMonth).Select(p => p.End).ToList();

        Assert.Equal(new DateOnly(2026, 2, 28), ends[0]);
        Assert.Equal(new DateOnly(2026, 3, 31), ends[1]);
        Assert.Equal(new DateOnly(2026, 4, 30), ends[2]);
        Assert.Equal(new DateOnly(2026, 5, 31), ends[3]);
        Assert.Equal(new DateOnly(2027, 1, 31), ends[^1]);
    }

    [Fact]
    public void Periods_Ots_IsASingleThreeMonthPeriod()
    {
        var period = Assert.Single(BondSchedule.Periods(TreasuryBondType.Ots, EndOfMonth));

        Assert.Equal(1, period.Index);
        Assert.Equal(EndOfMonth, period.Start);
        Assert.Equal(new DateOnly(2026, 4, 30), period.End);
    }

    [Fact]
    public void Periods_EdoBoughtOnLeapDay_EndsOnTheSameDayOfEachYearNotChained()
    {
        var periods = BondSchedule.Periods(TreasuryBondType.Edo, new DateOnly(2024, 2, 29));

        Assert.Equal(new DateOnly(2025, 2, 28), periods[0].End);
        Assert.Equal(new DateOnly(2026, 2, 28), periods[1].End);
        Assert.Equal(new DateOnly(2027, 2, 28), periods[2].End);
        Assert.Equal(new DateOnly(2028, 2, 29), periods[3].End);
        Assert.Equal(new DateOnly(2034, 2, 28), periods[^1].End);
    }

    [Theory]
    [InlineData(TreasuryBondType.Ots, false)]
    [InlineData(TreasuryBondType.Ror, true)]
    [InlineData(TreasuryBondType.Dor, true)]
    [InlineData(TreasuryBondType.Tos, false)]
    [InlineData(TreasuryBondType.Coi, true)]
    [InlineData(TreasuryBondType.Edo, false)]
    [InlineData(TreasuryBondType.Ros, false)]
    [InlineData(TreasuryBondType.Rod, false)]
    public void IsCoupon_EveryType_OnlyRorDorAndCoi(TreasuryBondType type, bool expected)
    {
        Assert.Equal(expected, BondSchedule.IsCoupon(type));
    }

    [Theory]
    [InlineData(TreasuryBondType.Ots, true)]
    [InlineData(TreasuryBondType.Ror, false)]
    [InlineData(TreasuryBondType.Dor, false)]
    [InlineData(TreasuryBondType.Tos, true)]
    [InlineData(TreasuryBondType.Coi, false)]
    [InlineData(TreasuryBondType.Edo, false)]
    [InlineData(TreasuryBondType.Ros, false)]
    [InlineData(TreasuryBondType.Rod, false)]
    public void IsFixedRate_EveryType_OnlyOtsAndTos(TreasuryBondType type, bool expected)
    {
        Assert.Equal(expected, BondSchedule.IsFixedRate(type));
    }
}
