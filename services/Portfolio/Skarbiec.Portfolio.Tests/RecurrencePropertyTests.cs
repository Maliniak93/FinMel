using Skarbiec.Portfolio.Features.Planning.Recurrence;
using static Skarbiec.Portfolio.Tests.Fixtures.RecurrenceRules;

namespace Skarbiec.Portfolio.Tests;

public sealed class RecurrencePropertyTests
{
    private static readonly DateOnly Start = new(2011, 1, 1);

    public static TheoryData<int> Intervals()
    {
        var data = new TheoryData<int>();
        for (var interval = 1; interval <= 12; interval++)
        {
            data.Add(interval);
        }

        return data;
    }

    private static IEnumerable<MonthlyRecurrence> AllRules(int interval)
    {
        yield return LastBusinessDay(interval);
        foreach (var shift in Enum.GetValues<BusinessDayShift>())
        {
            for (var day = 1; day <= 31; day++)
            {
                yield return Day(day, shift, interval);
            }
        }
    }

    private static int MonthsBetween(YearMonth a, YearMonth b) => Math.Abs((a.Year - b.Year) * 12 + a.Month - b.Month);

    [Theory]
    [MemberData(nameof(Intervals))]
    public void EveryRuleAndMonth_KeepsTheScheduleInvariants(int interval)
    {
        foreach (var rule in AllRules(interval))
        {
            var seenNominalMonths = new HashSet<YearMonth>();
            for (var month = new YearMonth(2011, 1); month <= new YearMonth(2099, 12); month = month.AddMonths(1))
            {
                var occurrences = RecurrenceSchedule.OccurrencesIn(month, Start, null, _ => rule);

                foreach (var occurrence in occurrences)
                {
                    Assert.True(seenNominalMonths.Add(occurrence.NominalMonth), $"{rule}: nominal {occurrence.NominalMonth} twice");
                    Assert.Equal(month, YearMonth.From(occurrence.Date));
                    Assert.True(MonthsBetween(month, occurrence.NominalMonth) <= 1, $"{rule}: {occurrence}");

                    var shifted = rule.Shift != BusinessDayShift.None || rule.DayKind == RecurrenceDayKind.LastBusinessDay;
                    if (shifted)
                    {
                        Assert.True(PolishHolidayCalendar.IsBusinessDay(occurrence.Date), $"{rule}: {occurrence}");
                    }

                    if (rule.Shift == BusinessDayShift.None && rule.DayKind == RecurrenceDayKind.DayOfMonth
                        && rule.DayOfMonth <= DateTime.DaysInMonth(occurrence.NominalMonth.Year, occurrence.NominalMonth.Month))
                    {
                        Assert.Equal(
                            new DateOnly(occurrence.NominalMonth.Year, occurrence.NominalMonth.Month, rule.DayOfMonth!.Value),
                            occurrence.Date);
                    }
                }
            }
        }
    }
}
