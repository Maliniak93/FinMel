using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;

namespace Skarbiec.Portfolio.Features.RecurringExpenses;

internal static class RecurringExpenseVersions
{
    // The latest version effective by the nominal month, else the first one (an expense always has at least one).
    public static RecurringExpenseVersion InForce(this RecurringExpense expense, YearMonth nominal)
    {
        var ordered = expense.Versions.OrderBy(v => v.EffectiveFrom).ToList();

        return ordered.LastOrDefault(v => v.EffectiveFrom <= nominal.FirstDay) ?? ordered[0];
    }

    // The version a change effective from the month edits in place: the first one for any month up to its own, else the one starting that month; null when a new version is needed.
    public static RecurringExpenseVersion? EditableFrom(this RecurringExpense expense, YearMonth effective)
    {
        var first = expense.Versions.OrderBy(v => v.EffectiveFrom).First();

        return effective <= YearMonth.From(first.EffectiveFrom)
            ? first
            : expense.Versions.FirstOrDefault(v => v.EffectiveFrom == effective.FirstDay);
    }

    public static MonthlyRecurrence ToRule(this RecurringExpenseVersion version) =>
        new(version.IntervalMonths, version.DayKind, version.DayOfMonth, version.Shift);

    // The date and amount the current rule gives the nominal month, null when the rule does not generate that payment.
    public static (DateOnly Date, decimal Amount)? ComputedAt(this RecurringExpense expense, YearMonth nominal)
    {
        var version = expense.InForce(nominal);
        var rule = version.ToRule();
        if (!rule.IsDue(nominal, YearMonth.From(expense.StartDate)))
        {
            return null;
        }

        var date = rule.DateFor(nominal);

        return date >= expense.StartDate && (expense.EndDate is null || date <= expense.EndDate) ? (date, version.Amount) : null;
    }

    public static ReservePlan ToReservePlan(this RecurringExpense expense) =>
        new(expense.StartDate, expense.EndDate, YearMonth.From(expense.CreatedOn), nominal =>
        {
            var version = expense.InForce(nominal);

            return new ReserveTerms(version.Amount, version.ToRule());
        });
}
