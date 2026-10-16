using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;

namespace Skarbiec.Portfolio.Tests.Fixtures;

internal static class ReservePlans
{
    public static YearMonth Month(int year, int month) => new(year, month);

    public static ReservePlan Plan(
        DateOnly start, YearMonth createdMonth, DateOnly? end, params (YearMonth From, ReserveTerms Terms)[] versions)
    {
        var ordered = versions.OrderBy(v => v.From).ToArray();
        return new ReservePlan(start, end, createdMonth, month => TermsIn(ordered, month));
    }

    public static ReservePlan Plan(DateOnly start, YearMonth createdMonth, ReserveTerms terms, DateOnly? end = null) =>
        new(start, end, createdMonth, _ => terms);

    public static ReserveTerms TermsIn((YearMonth From, ReserveTerms Terms)[] orderedVersions, YearMonth month)
    {
        var current = orderedVersions[0].Terms;
        foreach (var (from, terms) in orderedVersions)
        {
            if (from <= month)
            {
                current = terms;
            }
        }

        return current;
    }

    public static IEnumerable<YearMonth> MonthsFrom(YearMonth first, int count) =>
        Enumerable.Range(0, count).Select(first.AddMonths);
}
