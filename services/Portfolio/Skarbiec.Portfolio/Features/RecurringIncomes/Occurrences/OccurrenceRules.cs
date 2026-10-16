using System.Globalization;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

internal static class OccurrenceRules
{
    private static readonly YearMonth FirstMonth = YearMonth.From(RecurringIncomeLimits.FirstDay);
    private static readonly YearMonth LastMonth = YearMonth.From(RecurringIncomeLimits.LastDay);

    public static Result<YearMonth> ParseMonth(string month)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var nominal = YearMonth.From(parsed);

        return nominal < FirstMonth || nominal > LastMonth ? PlanningErrors.InvalidMonth(month) : nominal;
    }

    public static RecurringIncomeOccurrence? Freeze(this RecurringIncome income, YearMonth nominal, OccurrenceStatus status)
    {
        if (income.ComputedAt(nominal) is not { } computed)
        {
            return null;
        }

        return new RecurringIncomeOccurrence
        {
            Id = Guid.NewGuid(),
            RecurringIncomeId = income.Id,
            NominalMonth = nominal.FirstDay,
            Status = status,
            PlannedDate = computed.Date,
            PlannedAmount = computed.Version.Amount,
            PlannedAccountId = computed.Version.AccountId
        };
    }

    // The date the income's current rule gives the nominal month, null when the rule does not generate that key.
    public static (DateOnly Date, RecurringIncomeVersion Version)? ComputedAt(this RecurringIncome income, YearMonth nominal)
    {
        var version = income.InForce(nominal);
        var rule = version.ToRule();
        if (!rule.IsDue(nominal, YearMonth.From(income.StartDate)))
        {
            return null;
        }

        var date = rule.DateFor(nominal);

        return date >= income.StartDate && (income.EndDate is null || date <= income.EndDate) ? (date, version) : null;
    }
}
