using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningYear;

public sealed class GetPlanningYearHandler(PlanningMonthLoader loader)
{
    public async Task<Result<PlanningYearResponse>> HandleAsync(int year, CancellationToken cancellationToken)
    {
        if (year < RecurringIncomeLimits.FirstDay.Year || year > RecurringIncomeLimits.LastDay.Year)
        {
            return PlanningErrors.InvalidYear(year);
        }

        var data = await loader.LoadAsync(new YearMonth(year, 1), new YearMonth(year, 12), cancellationToken);

        var currentMonth = YearMonth.From(data.Today);
        var starts = data.Incomes.Select(i => i.StartDate)
            .Concat(data.Forecast.Plans.Select(p => p.Expense.StartDate))
            .Concat(data.Forecast.OneOffIncomes.Select(i => i.Date))
            .Concat(data.Forecast.OneOffExpenses.Select(e => e.Date))
            .ToList();
        var firstYear = starts.Count == 0 ? currentMonth.Year : starts.Min().Year;
        var lastYear = currentMonth.Year + 1;
        if (year < firstYear || year > lastYear)
        {
            return PlanningErrors.InvalidYear(year);
        }

        var months = Enumerable.Range(1, 12)
            .Select(number => BuildMonth(data, new YearMonth(year, number), currentMonth))
            .ToList();

        return new PlanningYearResponse
        {
            Year = year,
            FirstYear = firstYear,
            LastYear = lastYear,
            Months = months,
            Totals = new PlanningYearTotalsResponse
            {
                IncomePlanPln = Sum(months, m => m.IncomePlanPln),
                IncomeActualPln = Sum(months, m => m.IncomeActualPln),
                DifferencePln = Sum(months, m => m.DifferencePln),
                CostPln = Sum(months, m => m.CostPln),
                FreeFundsPlanPln = Sum(months, m => m.FreeFundsPlanPln),
                FreeFundsForecastPln = Sum(months, m => m.FreeFundsForecastPln),
                ToInvestPln = Sum(months, m => m.ToInvestPln)
            }
        };
    }

    private static decimal Sum(IEnumerable<PlanningYearMonthResponse> months, Func<PlanningYearMonthResponse, decimal?> column) =>
        months.Sum(m => column(m) ?? 0m);

    private static PlanningYearMonthResponse BuildMonth(PlanningMonthData data, YearMonth month, YearMonth currentMonth)
    {
        var covered = data.Incomes.Any(i => Covers(i.StartDate, i.EndDate, month))
            || data.Forecast.Plans.Any(p => Covers(p.Expense.StartDate, p.Expense.EndDate, month))
            || data.Forecast.OneOffIncomes.Any(i => YearMonth.From(i.Date) == month)
            || data.Forecast.OneOffExpenses.Any(e => YearMonth.From(e.Date) == month);
        if (!covered)
        {
            return new PlanningYearMonthResponse
            {
                Month = month.ToString(),
                HasDefinitions = false,
                IncomePlanPln = null,
                IncomeActualPln = null,
                DifferencePln = null,
                CostPln = null,
                FreeFundsPlanPln = null,
                FreeFundsForecastPln = null,
                ToInvestPln = null,
                TransferPlanStatus = null,
                HasOverdue = false,
                Incomplete = false
            };
        }

        var read = PlanningMonthBuilder.Build(data, month);

        return new PlanningYearMonthResponse
        {
            Month = month.ToString(),
            HasDefinitions = true,
            IncomePlanPln = read.Incomes.Balance.PlanPln,
            IncomeActualPln = month > currentMonth ? null : read.Incomes.Balance.ActualPln,
            DifferencePln = read.Incomes.Balance.DifferencePln,
            CostPln = read.Expenses.CostPln,
            FreeFundsPlanPln = read.FreeFunds.PlanPln,
            FreeFundsForecastPln = read.FreeFunds.ForecastPln,
            ToInvestPln = read.Allocation.EffectiveFrom is null ? null : read.Allocation.ToInvestPln,
            TransferPlanStatus = read.TransferPlan.Status,
            HasOverdue = read.Incomes.Occurrences.Any(o => o.Overdue),
            Incomplete = read.FreeFunds.Incomplete
        };
    }

    private static bool Covers(DateOnly start, DateOnly? end, YearMonth month) =>
        start <= month.LastDay && (end is null || end >= month.FirstDay);
}
