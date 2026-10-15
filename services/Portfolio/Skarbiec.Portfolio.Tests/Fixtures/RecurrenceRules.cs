using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal static class RecurrenceRules
{
    public static MonthlyRecurrence Day(int day, BusinessDayShift shift = BusinessDayShift.None, int interval = 1) =>
        new(interval, RecurrenceDayKind.DayOfMonth, day, shift);

    public static MonthlyRecurrence LastBusinessDay(int interval = 1) =>
        new(interval, RecurrenceDayKind.LastBusinessDay, null, BusinessDayShift.None);
}
