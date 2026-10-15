using System.Globalization;

namespace Skarbiec.Portfolio.Features.Planning.Recurrence;

public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public YearMonth AddMonths(int months)
    {
        var index = Year * 12 + (Month - 1) + months;
        return new YearMonth(index / 12, index % 12 + 1);
    }

    public static YearMonth From(DateOnly date) => new(date.Year, date.Month);

    public static YearMonth Parse(string value)
    {
        var date = DateOnly.ParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture);
        return From(date);
    }

    public int CompareTo(YearMonth other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;

    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;

    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;

    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;
}
