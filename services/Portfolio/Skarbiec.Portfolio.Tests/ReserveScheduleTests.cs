using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Tests.Fixtures;
using static Skarbiec.Portfolio.Tests.Fixtures.ReservePlans;

namespace Skarbiec.Portfolio.Tests;

public sealed class ReserveScheduleTests
{
    private static readonly YearMonth Created = Month(2026, 10);

    private static ReserveTerms Insurance(decimal amount = 1200m, int interval = 12) =>
        new(amount, RecurrenceRules.Day(15, interval: interval));

    private static void AssertSingleReserve(ReservePlan plan, YearMonth from, int count, YearMonth nominal, decimal amount)
    {
        foreach (var month in MonthsFrom(from, count))
        {
            var reserve = Assert.Single(ReserveSchedule.ReservesIn(month, plan));
            Assert.Equal(nominal, reserve.NominalMonth);
            Assert.Equal(amount, reserve.Amount);
        }
    }

    private static void AssertNoReserves(ReservePlan plan, YearMonth from, int count)
    {
        foreach (var month in MonthsFrom(from, count))
        {
            Assert.Empty(ReserveSchedule.ReservesIn(month, plan));
        }
    }

    [Fact]
    public void MonthlyExpense_CostsItsPaymentEveryMonth()
    {
        var start = new DateOnly(2026, 1, 1);
        var living = Plan(start, Created, new ReserveTerms(3000m, RecurrenceRules.Day(1)));
        var bills = Plan(start, Created, new ReserveTerms(1000m, RecurrenceRules.Day(10)));

        foreach (var month in new[] { Month(2026, 9), Month(2026, 10), Month(2027, 2) })
        {
            Assert.Equal(3000m, ReserveSchedule.CostIn(month, living));
            Assert.Equal(1000m, ReserveSchedule.CostIn(month, bills));
        }
    }

    [Fact]
    public void YearlyExpense_CatchesUpFromTheCreatedMonth()
    {
        var plan = Plan(new DateOnly(2027, 3, 15), Created, Insurance());

        AssertNoReserves(plan, Month(2026, 9), 1);
        AssertSingleReserve(plan, Month(2026, 10), 6, Month(2027, 3), 200m);
        AssertSingleReserve(plan, Month(2027, 4), 12, Month(2028, 3), 100m);

        var payment = Assert.Single(ReserveSchedule.PaymentsIn(Month(2027, 3), plan));
        Assert.Equal(new DateOnly(2027, 3, 15), payment.Date);
        Assert.Equal(1200m, payment.Amount);
    }

    [Fact]
    public void PaymentBeforeCreatedMonth_ChargesItsOwnMonth()
    {
        var plan = Plan(new DateOnly(2025, 3, 15), Created, Insurance());

        var own = Assert.Single(ReserveSchedule.ReservesIn(Month(2026, 3), plan));
        Assert.Equal(1200m, own.Amount);
        AssertNoReserves(plan, Month(2026, 4), 6);
        AssertSingleReserve(plan, Month(2026, 10), 6, Month(2027, 3), 200m);
    }

    [Fact]
    public void RoundsTowardZero_PaymentMonthTakesTheRest()
    {
        var plan = Plan(new DateOnly(2026, 12, 20), Created, new ReserveTerms(1000m, RecurrenceRules.Day(20, interval: 3)));

        Assert.Equal(333.33m, Assert.Single(ReserveSchedule.ReservesIn(Month(2026, 10), plan)).Amount);
        Assert.Equal(333.33m, Assert.Single(ReserveSchedule.ReservesIn(Month(2026, 11), plan)).Amount);
        Assert.Equal(333.34m, Assert.Single(ReserveSchedule.ReservesIn(Month(2026, 12), plan)).Amount);
    }

    [Fact]
    public void AmountChange_KeepsEarlierMonths_LaterMonthsCatchUp()
    {
        var start = new DateOnly(2027, 3, 15);
        var changeFrom = Month(2027, 1);

        var raised = Plan(start, Created, null, (Month(2026, 1), Insurance()), (changeFrom, Insurance(1320m)));
        AssertSingleReserve(raised, Month(2026, 10), 3, Month(2027, 3), 200m);
        AssertSingleReserve(raised, Month(2027, 1), 3, Month(2027, 3), 240m);
        Assert.Equal(1320m, MonthsFrom(Month(2026, 10), 6).Sum(m => ReserveSchedule.CostIn(m, raised)));

        var lowered = Plan(start, Created, null, (Month(2026, 1), Insurance()), (changeFrom, Insurance(300m)));
        AssertSingleReserve(lowered, Month(2026, 10), 3, Month(2027, 3), 200m);
        AssertSingleReserve(lowered, Month(2027, 1), 3, Month(2027, 3), -100m);
        Assert.Equal(300m, MonthsFrom(Month(2026, 10), 6).Sum(m => ReserveSchedule.CostIn(m, lowered)));
    }

    [Fact]
    public void IntervalChange_KeepsEarlierMonths()
    {
        var start = new DateOnly(2027, 3, 15);
        var original = (Month(2026, 1), Insurance());

        var toSixMonths = Plan(start, Created, null, original, (Month(2027, 1), Insurance(interval: 6)));
        AssertSingleReserve(toSixMonths, Month(2026, 10), 6, Month(2027, 3), 200m);
        AssertSingleReserve(toSixMonths, Month(2027, 4), 6, Month(2027, 9), 200m);

        var toThreeMonths = Plan(start, Created, null, original, (Month(2028, 1), Insurance(interval: 3)));
        AssertSingleReserve(toThreeMonths, Month(2027, 4), 9, Month(2028, 3), 100m);
        AssertSingleReserve(toThreeMonths, Month(2028, 1), 3, Month(2028, 3), 100m);
        AssertSingleReserve(toThreeMonths, Month(2028, 4), 3, Month(2028, 6), 400m);

        var toFiveMonths = Plan(start, Created, null, original, (Month(2028, 1), Insurance(interval: 5)));
        AssertSingleReserve(toFiveMonths, Month(2027, 4), 9, Month(2028, 3), 100m);
        var own = Assert.Single(ReserveSchedule.ReservesIn(Month(2028, 1), toFiveMonths));
        Assert.Equal(Month(2028, 1), own.NominalMonth);
        Assert.Equal(1200m, own.Amount);
    }

    [Fact]
    public void EndDate_RemovesReservesOfPaymentsThatNeverHappen()
    {
        var start = new DateOnly(2027, 3, 15);

        var endsEarly = Plan(start, Created, Insurance(), new DateOnly(2026, 12, 31));
        AssertNoReserves(endsEarly, Month(2026, 9), 22);

        var endsAtLastPayment = Plan(start, Created, Insurance(), new DateOnly(2028, 3, 31));
        var last = Assert.Single(ReserveSchedule.ReservesIn(Month(2028, 3), endsAtLastPayment));
        Assert.Equal(Month(2028, 3), last.NominalMonth);
        Assert.Equal(100m, last.Amount);
        AssertNoReserves(endsAtLastPayment, Month(2028, 4), 1);
    }

    [Fact]
    public void TwoPaymentsInOneMonth()
    {
        var plan = Plan(
            new DateOnly(2026, 7, 1),
            Month(2026, 6),
            new ReserveTerms(500m, RecurrenceRules.Day(1, BusinessDayShift.PreviousBusinessDay)));

        Assert.Equal(1000m, ReserveSchedule.CostIn(Month(2026, 7), plan));
        Assert.Equal(0m, ReserveSchedule.CostIn(Month(2026, 8), plan));
    }
}
