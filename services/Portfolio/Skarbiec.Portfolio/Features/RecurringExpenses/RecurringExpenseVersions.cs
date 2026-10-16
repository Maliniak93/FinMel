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

    public static ReservePlan ToReservePlan(this RecurringExpense expense) =>
        new(expense.StartDate, expense.EndDate, YearMonth.From(expense.CreatedOn), nominal =>
        {
            var version = expense.InForce(nominal);

            return new ReserveTerms(
                version.Amount,
                new MonthlyRecurrence(version.IntervalMonths, version.DayKind, version.DayOfMonth, version.Shift));
        });
}
