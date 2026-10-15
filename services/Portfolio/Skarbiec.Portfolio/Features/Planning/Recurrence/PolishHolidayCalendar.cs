namespace Skarbiec.Portfolio.Features.Planning.Recurrence;

public static class PolishHolidayCalendar
{
    private const int FirstSupportedYear = 2010;
    private const int LastSupportedYear = 2100;
    private const int EpiphanyFromYear = 2011;
    private const int ChristmasEveFromYear = 2025;

    // Anonymous Gregorian (Meeus) algorithm.
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    public static bool IsHoliday(DateOnly date)
    {
        EnsureSupported(date);

        var year = date.Year;
        switch (date.Month, date.Day)
        {
            case (1, 1):
            case (5, 1):
            case (5, 3):
            case (8, 15):
            case (11, 1):
            case (11, 11):
            case (12, 25):
            case (12, 26):
                return true;
            case (1, 6):
                return year >= EpiphanyFromYear;
            case (12, 24):
                return year >= ChristmasEveFromYear;
        }

        var easter = EasterSunday(year);
        return date == easter
            || date == easter.AddDays(1)
            || date == easter.AddDays(49)
            || date == easter.AddDays(60);
    }

    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    public static DateOnly PreviousBusinessDay(DateOnly date)
    {
        while (!IsBusinessDay(date))
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    public static DateOnly NextBusinessDay(DateOnly date)
    {
        while (!IsBusinessDay(date))
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static void EnsureSupported(DateOnly date)
    {
        if (date.Year is < FirstSupportedYear or > LastSupportedYear)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date), date, $"Only years {FirstSupportedYear}-{LastSupportedYear} are supported.");
        }
    }
}
