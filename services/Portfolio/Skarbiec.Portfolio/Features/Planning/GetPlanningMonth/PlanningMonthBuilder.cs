using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Features.RecurringExpenses;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal static class PlanningMonthBuilder
{
    // Twice the longest interval.
    private const int NextPaymentLookAheadMonths = 24;

    public static PlanningMonthResponse Build(PlanningMonthData data, YearMonth viewed)
    {
        var forecast = data.Forecast;
        var accounts = data.Accounts;
        var rates = data.Rates;
        var today = data.Today;

        var occurrences = forecast.OccurrencesIn(viewed, rates, today);

        var definitions = data.Incomes
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(income =>
            {
                var version = income.InForce(viewed);
                var account = accounts[version.AccountId];
                var next = income.Versions
                    .Where(v => v.EffectiveFrom > viewed.FirstDay)
                    .OrderBy(v => v.EffectiveFrom)
                    .FirstOrDefault();
                var nextAccount = next is null ? null : accounts[next.AccountId];

                return new IncomeDefinitionResponse
                {
                    Id = income.Id,
                    Name = income.Name,
                    StartDate = income.StartDate,
                    EndDate = income.EndDate,
                    Status = income.StartDate > viewed.LastDay ? RecurringIncomeStatus.Upcoming
                        : income.EndDate < viewed.FirstDay ? RecurringIncomeStatus.Ended
                        : RecurringIncomeStatus.Active,
                    Amount = version.Amount,
                    Currency = account.Currency,
                    AccountId = account.Id,
                    AccountName = account.Name,
                    AccountArchived = account.IsArchived,
                    IntervalMonths = version.IntervalMonths,
                    DayKind = version.DayKind,
                    DayOfMonth = version.DayOfMonth,
                    Shift = version.Shift,
                    EffectiveFrom = YearMonth.From(version.EffectiveFrom).ToString(),
                    NextChange = next is null || nextAccount is null
                        ? null
                        : new IncomeNextChangeResponse
                        {
                            EffectiveFrom = YearMonth.From(next.EffectiveFrom).ToString(),
                            Amount = next.Amount,
                            Currency = nextAccount.Currency,
                            AccountName = nextAccount.Name,
                            IntervalMonths = next.IntervalMonths,
                            DayKind = next.DayKind,
                            DayOfMonth = next.DayOfMonth,
                            Shift = next.Shift
                        }
                };
            })
            .ToList();

        var oneOffIncomes = forecast.OneOffIncomesIn(viewed, rates, today);
        var balance = PlanningForecast.BalanceOf(occurrences, oneOffIncomes);
        var fxUnavailable = forecast.ForeignIn(viewed).Any(c => !rates.ContainsKey(c));

        var expenses = BuildExpenses(viewed, forecast);
        var viewedFigures = new MonthFigures(balance.ActualPln + balance.PendingPln - expenses.CostPln, expenses.CostPln);

        return new PlanningMonthResponse
        {
            Incomes = new PlanningIncomesResponse
            {
                Occurrences = occurrences,
                Balance = balance,
                FxUnavailable = fxUnavailable,
                Definitions = definitions,
                OneOffs = oneOffIncomes
            },
            Expenses = expenses,
            FreeFunds = new FreeFundsResponse
            {
                PlanPln = balance.PlanPln - expenses.CostPln,
                ForecastPln = viewedFigures.Forecast,
                Incomplete = fxUnavailable
            },
            NonBusinessDays = Enumerable.Range(0, viewed.LastDay.Day)
                .Select(offset => viewed.FirstDay.AddDays(offset))
                .Where(day => !PolishHolidayCalendar.IsBusinessDay(day))
                .ToList()
        };
    }

    private static PlanningExpensesResponse BuildExpenses(YearMonth viewed, PlanningForecast forecast)
    {
        var plans = forecast.Plans;
        var payments = plans
            .SelectMany(p => ReserveSchedule.PaymentsIn(viewed, p.Plan).Select(payment => new ExpensePaymentResponse
            {
                RecurringExpenseId = p.Expense.Id,
                NominalMonth = payment.NominalMonth.ToString(),
                Date = payment.Date,
                Name = p.Expense.Name,
                Amount = payment.Amount
            }))
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var definitions = plans
            .OrderBy(p => p.Expense.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                var version = p.Expense.InForce(viewed);
                var next = p.Expense.Versions
                    .Where(v => v.EffectiveFrom > viewed.FirstDay)
                    .OrderBy(v => v.EffectiveFrom)
                    .FirstOrDefault();

                return new ExpenseDefinitionResponse
                {
                    Id = p.Expense.Id,
                    Name = p.Expense.Name,
                    StartDate = p.Expense.StartDate,
                    EndDate = p.Expense.EndDate,
                    Status = p.Expense.StartDate > viewed.LastDay ? RecurringIncomeStatus.Upcoming
                        : p.Expense.EndDate < viewed.FirstDay ? RecurringIncomeStatus.Ended
                        : RecurringIncomeStatus.Active,
                    Amount = version.Amount,
                    IntervalMonths = version.IntervalMonths,
                    DayKind = version.DayKind,
                    DayOfMonth = version.DayOfMonth,
                    Shift = version.Shift,
                    ReservePln = ReserveSchedule.CostIn(viewed, p.Plan),
                    NextPayment = NextPayment(viewed, p.Plan),
                    EffectiveFrom = YearMonth.From(version.EffectiveFrom).ToString(),
                    NextChange = next is null
                        ? null
                        : new ExpenseNextChangeResponse
                        {
                            EffectiveFrom = YearMonth.From(next.EffectiveFrom).ToString(),
                            Amount = next.Amount,
                            IntervalMonths = next.IntervalMonths,
                            DayKind = next.DayKind,
                            DayOfMonth = next.DayOfMonth,
                            Shift = next.Shift
                        }
                };
            })
            .ToList();

        return new PlanningExpensesResponse
        {
            Payments = payments,
            CostPln = forecast.CostIn(viewed),
            Definitions = definitions,
            OneOffs = forecast.OneOffExpensesIn(viewed)
        };
    }

    private static ExpenseNextPaymentResponse? NextPayment(YearMonth viewed, ReservePlan plan)
    {
        for (var offset = 0; offset < NextPaymentLookAheadMonths; offset++)
        {
            var next = ReserveSchedule.PaymentsIn(viewed.AddMonths(offset), plan)
                .OrderBy(p => p.Date)
                .FirstOrDefault();
            if (next is not null)
            {
                return new ExpenseNextPaymentResponse { Date = next.Date, Amount = next.Amount };
            }
        }

        return null;
    }
}
