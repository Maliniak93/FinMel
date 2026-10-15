namespace Skarbiec.Portfolio.Features.Planning.Recurrence;

public sealed record RecurrenceOccurrence(YearMonth NominalMonth, DateOnly Date);

public static class RecurrenceSchedule
{
    // A shift moves a date by far less than a month, so the neighbouring nominal months are the only candidates.
    public static IReadOnlyList<RecurrenceOccurrence> OccurrencesIn(
        YearMonth month, DateOnly start, DateOnly? end, Func<YearMonth, MonthlyRecurrence> ruleFor)
    {
        var anchor = YearMonth.From(start);
        var occurrences = new List<RecurrenceOccurrence>();

        for (var offset = -1; offset <= 1; offset++)
        {
            var nominal = month.AddMonths(offset);
            var rule = ruleFor(nominal);
            if (!rule.IsDue(nominal, anchor))
            {
                continue;
            }

            var date = rule.DateFor(nominal);
            if (YearMonth.From(date) == month && date >= start && (end is null || date <= end))
            {
                occurrences.Add(new RecurrenceOccurrence(nominal, date));
            }
        }

        return occurrences;
    }
}
