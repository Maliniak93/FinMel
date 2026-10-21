using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Tests;

public sealed class PolishHolidayCalendarTests
{
    public static TheoryData<int, int, int> KnownEasterDates() => new()
    {
        { 2011, 4, 24 }, { 2013, 3, 31 }, { 2016, 3, 27 }, { 2019, 4, 21 }, { 2024, 3, 31 },
        { 2026, 4, 5 }, { 2035, 3, 25 },
    };

    [Theory]
    [MemberData(nameof(KnownEasterDates))]
    public void EasterSunday_MatchesKnownDates(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), PolishHolidayCalendar.EasterSunday(year));
    }

    [Fact]
    public void Holidays2026_AreExactlyTheStatutoryList()
    {
        DateOnly[] expected =
        [
            new(2026, 1, 1), new(2026, 1, 6), new(2026, 4, 5), new(2026, 4, 6), new(2026, 5, 1),
            new(2026, 5, 3), new(2026, 5, 24), new(2026, 6, 4), new(2026, 8, 15), new(2026, 11, 1),
            new(2026, 11, 11), new(2026, 12, 24), new(2026, 12, 25), new(2026, 12, 26),
        ];

        var actual = new List<DateOnly>();
        for (var day = new DateOnly(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            if (PolishHolidayCalendar.IsHoliday(day))
            {
                actual.Add(day);
            }
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChristmasEveAndEpiphany_FollowTheirIntroductionYear()
    {
        Assert.False(PolishHolidayCalendar.IsHoliday(new DateOnly(2024, 12, 24)));
        Assert.True(PolishHolidayCalendar.IsHoliday(new DateOnly(2025, 12, 24)));
        Assert.False(PolishHolidayCalendar.IsHoliday(new DateOnly(2010, 1, 6)));
        Assert.True(PolishHolidayCalendar.IsHoliday(new DateOnly(2011, 1, 6)));
    }
}
