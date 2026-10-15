namespace Skarbiec.Portfolio.Features.Planning.Recurrence;

public enum RecurrenceDayKind
{
    DayOfMonth = 0,
    LastBusinessDay = 1,
}

public enum BusinessDayShift
{
    None = 0,
    PreviousBusinessDay = 1,
    NextBusinessDay = 2,
}

public sealed record MonthlyRecurrence(int IntervalMonths, RecurrenceDayKind DayKind, int? DayOfMonth, BusinessDayShift Shift)
{
    public bool IsDue(YearMonth nominal, YearMonth anchor)
    {
        var distance = (nominal.Year - anchor.Year) * 12 + nominal.Month - anchor.Month;
        return distance >= 0 && distance % IntervalMonths == 0;
    }

    public DateOnly DateFor(YearMonth nominal)
    {
        if (DayKind == RecurrenceDayKind.LastBusinessDay)
        {
            return PolishHolidayCalendar.PreviousBusinessDay(nominal.LastDay);
        }

        var day = Math.Min(DayOfMonth ?? 1, nominal.LastDay.Day);
        var date = new DateOnly(nominal.Year, nominal.Month, day);

        return Shift switch
        {
            BusinessDayShift.PreviousBusinessDay => PolishHolidayCalendar.PreviousBusinessDay(date),
            BusinessDayShift.NextBusinessDay => PolishHolidayCalendar.NextBusinessDay(date),
            _ => date,
        };
    }
}
