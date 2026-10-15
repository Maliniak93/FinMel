using Skarbiec.Portfolio.Features.Planning.Recurrence;
using static Skarbiec.Portfolio.Tests.Fixtures.RecurrenceRules;

namespace Skarbiec.Portfolio.Tests;

public sealed class RecurrenceScheduleTests
{
    private static IReadOnlyList<RecurrenceOccurrence> In(
        int year, int month, MonthlyRecurrence rule, DateOnly start, DateOnly? end = null) =>
        RecurrenceSchedule.OccurrencesIn(new YearMonth(year, month), start, end, _ => rule);

    [Fact]
    public void ShiftAcrossMonthBoundary_BelongsToTheShiftedMonth()
    {
        var rule = Day(1, BusinessDayShift.PreviousBusinessDay);
        var start = new DateOnly(2026, 7, 1);

        var july = In(2026, 7, rule, start);
        var august = In(2026, 8, rule, start);

        Assert.Equal(
            [
                new RecurrenceOccurrence(new YearMonth(2026, 7), new DateOnly(2026, 7, 1)),
                new RecurrenceOccurrence(new YearMonth(2026, 8), new DateOnly(2026, 7, 31)),
            ],
            july);
        Assert.Empty(august);
    }

    [Fact]
    public void StartAndEnd_AreInclusiveOnTheShiftedDate()
    {
        var rule = Day(10);
        var start = new DateOnly(2024, 6, 10);
        var end = new DateOnly(2032, 3, 15);

        Assert.Equal(new DateOnly(2032, 3, 10), Assert.Single(In(2032, 3, rule, start, end)).Date);
        Assert.Empty(In(2032, 4, rule, start, end));

        var laterStart = new DateOnly(2026, 10, 20);
        Assert.Empty(In(2026, 10, rule, laterStart));
        Assert.Equal(new DateOnly(2026, 11, 10), Assert.Single(In(2026, 11, rule, laterStart)).Date);

        var onStart = new DateOnly(2026, 10, 10);
        Assert.Equal(onStart, Assert.Single(In(2026, 10, rule, onStart)).Date);
        Assert.Equal(new DateOnly(2026, 11, 10), Assert.Single(In(2026, 11, rule, onStart, new DateOnly(2026, 11, 10))).Date);
        Assert.Empty(In(2026, 11, rule, onStart, new DateOnly(2026, 11, 9)));

        var shifted = Day(10, BusinessDayShift.PreviousBusinessDay);
        Assert.Equal(new DateOnly(2026, 10, 9), Assert.Single(In(2026, 10, shifted, new DateOnly(2026, 10, 9))).Date);
        Assert.Empty(In(2026, 10, shifted, new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void Interval_IsAnchoredAtTheStartMonth()
    {
        var yearly = Day(25, interval: 12);
        var yearlyStart = new DateOnly(2027, 3, 1);
        var yearlyDates = Enumerable.Range(2027, 2)
            .SelectMany(year => Enumerable.Range(1, 12).SelectMany(month => In(year, month, yearly, yearlyStart)))
            .Select(o => o.Date);

        Assert.Equal([new DateOnly(2027, 3, 25), new DateOnly(2028, 3, 25)], yearlyDates);

        var quarterly = Day(15, interval: 3);
        var quarterlyStart = new DateOnly(2026, 1, 15);
        var quarterlyMonths = Enumerable.Range(1, 12)
            .SelectMany(month => In(2026, month, quarterly, quarterlyStart))
            .Select(o => o.Date.Month);

        Assert.Equal([1, 4, 7, 10], quarterlyMonths);
    }

    [Fact]
    public void UsesTheRuleOfEachNominalMonth()
    {
        var ruleA = Day(10);
        var ruleB = Day(20);
        MonthlyRecurrence RuleFor(YearMonth nominal) => nominal < new YearMonth(2026, 8) ? ruleA : ruleB;
        var start = new DateOnly(2026, 1, 1);

        var july = RecurrenceSchedule.OccurrencesIn(new YearMonth(2026, 7), start, null, RuleFor);
        var august = RecurrenceSchedule.OccurrencesIn(new YearMonth(2026, 8), start, null, RuleFor);

        Assert.Equal([new RecurrenceOccurrence(new YearMonth(2026, 7), new DateOnly(2026, 7, 10))], july);
        Assert.Equal([new RecurrenceOccurrence(new YearMonth(2026, 8), new DateOnly(2026, 8, 20))], august);
    }
}
