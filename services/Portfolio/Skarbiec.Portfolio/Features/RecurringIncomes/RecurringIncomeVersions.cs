using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringIncomes;

internal static class RecurringIncomeVersions
{
    // The latest version effective by the nominal month, else the first one (an income always has at least one).
    public static RecurringIncomeVersion InForce(this RecurringIncome income, YearMonth nominal)
    {
        var ordered = income.Versions.OrderBy(v => v.EffectiveFrom).ToList();

        return ordered.LastOrDefault(v => v.EffectiveFrom <= nominal.FirstDay) ?? ordered[0];
    }

    public static MonthlyRecurrence ToRule(this RecurringIncomeVersion version) =>
        new(version.IntervalMonths, version.DayKind, version.DayOfMonth, version.Shift);
}
