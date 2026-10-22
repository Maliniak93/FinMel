using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Features.Planning.Split;
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
            Split = BuildSplit(data, viewed, viewedFigures.Forecast),
            NonBusinessDays = Enumerable.Range(0, viewed.LastDay.Day)
                .Select(offset => viewed.FirstDay.AddDays(offset))
                .Where(day => !PolishHolidayCalendar.IsBusinessDay(day))
                .ToList()
        };
    }

    private static PlanningSplitResponse? BuildSplit(PlanningMonthData data, YearMonth viewed, decimal freeFundsPln)
    {
        var forecast = data.Forecast;
        if (viewed < forecast.CurrentMonth || forecast.Groups.Count == 0)
        {
            return null;
        }

        var replica = data.Replica;
        decimal ValueOf(Guid groupId) => replica?.ByGroup
            .Where(g => g.GroupId == groupId)
            .SelectMany(g => g.ByAssetClass)
            .Sum(c => c.ValuePln) ?? 0m;

        var cushionGroup = forecast.Groups.FirstOrDefault(g => g.Kind == AssetGroupKind.Cushion);
        SplitCushionResponse? cushion = null;
        decimal missing = 0m;
        if (cushionGroup is not null)
        {
            var earlierFreeFunds = Enumerable
                .Range(0, Math.Max(0, MonthsBetween(forecast.CurrentMonth, viewed)))
                .Select(offset => forecast.FiguresIn(forecast.CurrentMonth.AddMonths(offset), data.Rates, data.Today).Forecast);
            var target = cushionGroup.TargetPln ?? 0m;
            var level = CushionLedger.LevelAfter(ValueOf(cushionGroup.Id), target, cushionGroup.MonthlyContributionPln, earlierFreeFunds);
            missing = Math.Max(0m, target - level);
            cushion = new SplitCushionResponse
            {
                GroupId = cushionGroup.Id,
                Name = cushionGroup.Name,
                LevelPln = level,
                TargetPln = target,
                MissingPln = missing,
                AllocationPln = 0m
            };
        }

        var regular = forecast.Groups
            .Where(g => g.Kind == AssetGroupKind.Regular)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var split = GroupSplitCalculator.Split(
            freeFundsPln,
            missing,
            [.. regular.Select(g => new GroupShareInput(g.Id, g.Name, g.SharePercent ?? 0m))]);

        var allocations = split.Groups.ToDictionary(a => a.GroupId, a => a.AllocationPln);

        return new PlanningSplitResponse
        {
            FreeFundsPln = freeFundsPln,
            DeficitPln = freeFundsPln <= 0m ? split.DeficitPln : null,
            Cushion = cushion is null ? null : cushion with { AllocationPln = split.CushionPln },
            Groups =
            [
                .. regular.Select(g =>
                {
                    var contribution = g.MonthlyContributionPln;
                    var values = (replica?.ByGroup ?? [])
                        .Where(v => v.GroupId == g.Id)
                        .SelectMany(v => v.ByAssetClass)
                        .GroupBy(c => c.AssetClass)
                        .ToDictionary(c => c.Key, c => c.Sum(v => v.ValuePln));

                    return new SplitGroupResponse
                    {
                        GroupId = g.Id,
                        Name = g.Name,
                        SharePercent = g.SharePercent ?? 0m,
                        ContributionPln = contribution,
                        AllocationPln = allocations[g.Id],
                        Breakdown = contribution > 0m && g.ClassAllocation.Count > 0
                            ? [.. ContributionBreakdown.Compute(contribution, g.ClassAllocation, values)
                                .Select(p => new SplitClassAmountResponse { AssetClass = p.AssetClass, AmountPln = p.AmountPln })]
                            : null
                    };
                })
            ],
            UnassignedPln = split.UnassignedPln,
            ValuationMissing = replica is null
        };
    }

    private static int MonthsBetween(YearMonth from, YearMonth to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

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
            GroupContributions =
            [
                .. forecast.ContributingGroupsIn(viewed)
                    .Select(g => new GroupContributionResponse { GroupId = g.Id, Name = g.Name, AmountPln = g.MonthlyContributionPln })
            ],
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
