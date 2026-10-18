using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Features.RecurringExpenses;
using Skarbiec.Portfolio.Features.RecurringIncomes;
using Skarbiec.Portfolio.Features.TransferPlan;

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

        var balance = PlanningForecast.BalanceOf(occurrences);
        var fxUnavailable = forecast.ForeignIn(viewed).Any(c => !rates.ContainsKey(c));

        var expenses = BuildExpenses(viewed, forecast.Plans);
        var viewedFigures = new MonthFigures(balance.ActualPln + balance.PendingPln - expenses.CostPln, expenses.CostPln);
        var allocation = BuildAllocation(
            viewed,
            viewedFigures,
            fxUnavailable,
            accounts,
            data.Versions,
            data.Overrides,
            data.Kinds,
            forecast.Plans,
            walked => walked == viewed ? viewedFigures : forecast.FiguresIn(walked, rates, today));

        var transferPlan = TransferPlanPreview.Build(
            viewed,
            YearMonth.From(today),
            data.PlanStart,
            allocation,
            accounts,
            data.TargetReplica,
            data.ValuationReplica,
            data.MinimumTransfer,
            data.TransferPlans);

        return new PlanningMonthResponse
        {
            Incomes = new PlanningIncomesResponse
            {
                Occurrences = occurrences,
                Balance = balance,
                FxUnavailable = fxUnavailable,
                Definitions = definitions
            },
            Expenses = expenses,
            FreeFunds = new FreeFundsResponse
            {
                PlanPln = balance.PlanPln - expenses.CostPln,
                ForecastPln = viewedFigures.Forecast,
                Incomplete = fxUnavailable
            },
            Allocation = allocation,
            TransferPlan = transferPlan
        };
    }

    private static AllocationResponse BuildAllocation(
        YearMonth viewed,
        MonthFigures figures,
        bool incomplete,
        Dictionary<Guid, Account> accounts,
        List<AllocationPlanVersion> versions,
        List<AllocationBalanceOverride> overrides,
        IReadOnlyDictionary<Guid, BucketKind> kinds,
        IReadOnlyList<(RecurringExpense Expense, ReservePlan Plan)> plans,
        Func<YearMonth, MonthFigures> figuresFor)
    {
        var ledger = AllocationLedger.Compute(viewed, versions, overrides, kinds, figuresFor);
        var next = versions
            .Where(v => v.EffectiveFrom > viewed.FirstDay)
            .OrderBy(v => v.EffectiveFrom)
            .FirstOrDefault();

        var buckets = ledger.Buckets
            .Select(b => new AllocationBucketResponse
            {
                BucketId = b.Entry.BucketId,
                Name = b.Entry.Name,
                Kind = kinds[b.Entry.BucketId],
                ShareType = b.Entry.ShareType,
                Share = b.Entry.Share,
                Position = b.Entry.Position,
                IsRemainder = b.Entry.IsRemainder,
                AccountId = b.Entry.AccountId,
                AccountName = b.Entry.AccountId is { } accountId && accounts.TryGetValue(accountId, out var account) ? account.Name : null,
                AllocatedPln = b.Allocation.Allocated,
                Underfunded = b.Allocation.Underfunded,
                LimitReached = b.Allocation.LimitReached,
                Limit = b.Limit is null
                    ? null
                    : new LimitResponse
                    {
                        Year = b.Limit.Year,
                        LimitPln = b.Limit.Limit,
                        ContributedStartPln = b.Limit.ContributedStart,
                        ContributedEndPln = b.Limit.ContributedEnd,
                        Reached = b.Limit.Reached,
                        Estimated = b.Limit.Estimated
                    },
                Adjustments = kinds[b.Entry.BucketId] is BucketKind.Cushion || kinds[b.Entry.BucketId].IsLimited()
                    ? overrides
                        .Where(o => o.BucketId == b.Entry.BucketId)
                        .OrderBy(o => o.Month)
                        .Select(o => new BalanceAdjustmentResponse { Month = YearMonth.From(o.Month).ToString(), AmountPln = o.Amount })
                        .ToList()
                    : null,
                Cushion = b.Cushion is null
                    ? null
                    : new CushionResponse
                    {
                        BalanceStartPln = b.Cushion.BalanceStart,
                        TargetPln = b.Cushion.Target,
                        TargetMonths = b.Cushion.TargetMonths,
                        BalanceEndPln = b.Cushion.BalanceEnd,
                        Reached = b.Cushion.Reached
                    }
            })
            .ToList();

        return new AllocationResponse
        {
            EffectiveFrom = ledger.InForce is null ? null : YearMonth.From(ledger.InForce.EffectiveFrom).ToString(),
            NextChange = next is null ? null : YearMonth.From(next.EffectiveFrom).ToString(),
            ForecastPln = figures.Forecast,
            Incomplete = incomplete,
            DeficitPln = ledger.Deficit,
            Buckets = buckets,
            ToInvestPln = buckets.Where(b => b.Kind == BucketKind.Investments || b.Kind.IsLimited()).Sum(b => b.AllocatedPln),
            Reserve = BuildReserve(viewed, plans, ledger.InForce?.ReserveAccountId, accounts)
        };
    }

    private static ReserveResponse? BuildReserve(
        YearMonth viewed,
        IReadOnlyList<(RecurringExpense Expense, ReservePlan Plan)> plans,
        Guid? reserveAccountId,
        Dictionary<Guid, Account> accounts)
    {
        var monthPln = plans
            .Where(p => p.Expense.InForce(viewed).IntervalMonths > 1)
            .Sum(p => ReserveSchedule.ReservesIn(viewed, p.Plan).Sum(r => r.Amount));

        var balancePln = 0m;
        foreach (var (_, plan) in plans)
        {
            for (var month = plan.CreatedMonth; month <= viewed; month = month.AddMonths(1))
            {
                balancePln += ReserveSchedule.CostIn(month, plan) - ReserveSchedule.PaymentsIn(month, plan).Sum(p => p.Amount);
            }
        }

        if (monthPln == 0m && balancePln == 0m && reserveAccountId is null)
        {
            return null;
        }

        return new ReserveResponse
        {
            MonthPln = monthPln,
            BalancePln = balancePln,
            AccountId = reserveAccountId,
            AccountName = reserveAccountId is { } id && accounts.TryGetValue(id, out var account) ? account.Name : null
        };
    }

    private static PlanningExpensesResponse BuildExpenses(
        YearMonth viewed, IReadOnlyList<(RecurringExpense Expense, ReservePlan Plan)> plans)
    {
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
            CostPln = plans.Sum(p => ReserveSchedule.CostIn(viewed, p.Plan)),
            Definitions = definitions
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
