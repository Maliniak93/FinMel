using Skarbiec.Portfolio.Features.Planning.Recurrence;
using static Skarbiec.Portfolio.Tests.Fixtures.RecurrenceRules;

namespace Skarbiec.Portfolio.Tests;

public sealed class MonthlyRecurrenceTests
{
    [Theory]
    [InlineData(31, BusinessDayShift.None, 2026, 2, 2026, 2, 28)]
    [InlineData(31, BusinessDayShift.PreviousBusinessDay, 2026, 2, 2026, 2, 27)]
    [InlineData(10, BusinessDayShift.PreviousBusinessDay, 2026, 10, 2026, 10, 9)]
    [InlineData(10, BusinessDayShift.PreviousBusinessDay, 2026, 11, 2026, 11, 10)]
    [InlineData(1, BusinessDayShift.NextBusinessDay, 2026, 11, 2026, 11, 2)]
    [InlineData(11, BusinessDayShift.NextBusinessDay, 2026, 11, 2026, 11, 12)]
    [InlineData(24, BusinessDayShift.NextBusinessDay, 2025, 12, 2025, 12, 29)]
    [InlineData(24, BusinessDayShift.NextBusinessDay, 2024, 12, 2024, 12, 24)]
    [InlineData(6, BusinessDayShift.PreviousBusinessDay, 2026, 1, 2026, 1, 5)]
    public void DateFor_ClampsAndShiftsOffNonBusinessDays(
        int day, BusinessDayShift shift, int year, int month, int expectedYear, int expectedMonth, int expectedDay)
    {
        var date = Day(day, shift).DateFor(new YearMonth(year, month));

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), date);
    }

    [Theory]
    [InlineData(2026, 10, 2026, 10, 30)]
    [InlineData(2026, 11, 2026, 11, 30)]
    [InlineData(2026, 12, 2026, 12, 31)]
    [InlineData(2027, 1, 2027, 1, 29)]
    [InlineData(2026, 1, 2026, 1, 30)]
    public void DateFor_LastBusinessDay(int year, int month, int expectedYear, int expectedMonth, int expectedDay)
    {
        var date = LastBusinessDay().DateFor(new YearMonth(year, month));

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), date);
    }
}
