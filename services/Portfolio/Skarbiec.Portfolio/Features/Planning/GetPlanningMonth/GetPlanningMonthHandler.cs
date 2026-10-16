using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.CashPlaces;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Features.RecurringExpenses;
using Skarbiec.Portfolio.Features.RecurringIncomes;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed class GetPlanningMonthHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    // Twice the longest interval.
    private const int NextPaymentLookAheadMonths = 24;

    private static readonly YearMonth FirstMonth = YearMonth.From(RecurringIncomeLimits.FirstDay);
    private static readonly YearMonth LastMonth = YearMonth.From(RecurringIncomeLimits.LastDay);

    public async Task<Result<PlanningMonthResponse>> HandleAsync(string month, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var viewed = YearMonth.From(parsed);
        if (viewed < FirstMonth || viewed > LastMonth)
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var incomes = await dbContext.RecurringIncomes
            .AsNoTracking()
            .Include(i => i.Versions)
            .ToListAsync(cancellationToken);

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        // A shift moves a date by less than a month, so a stored occurrence planned in the viewed month has a nominal month within one of it.
        var stored = await dbContext.RecurringIncomeOccurrences
            .AsNoTracking()
            .Where(o => o.NominalMonth >= viewed.AddMonths(-1).FirstDay && o.NominalMonth <= viewed.AddMonths(1).FirstDay)
            .ToListAsync(cancellationToken);
        var storedKeys = stored.Select(o => (o.RecurringIncomeId, o.NominalMonth)).ToHashSet();
        var incomesById = incomes.ToDictionary(i => i.Id);

        var computed = incomes
            .SelectMany(income => RecurrenceSchedule
                .OccurrencesIn(viewed, income.StartDate, income.EndDate, nominal => income.InForce(nominal).ToRule())
                .Where(occurrence => !storedKeys.Contains((income.Id, occurrence.NominalMonth.FirstDay)))
                .Select(occurrence =>
                {
                    var version = income.InForce(occurrence.NominalMonth);
                    return new PlannedRow(income, occurrence.NominalMonth, occurrence.Date, version.Amount, version.AccountId, null);
                }));

        var storedRows = stored
            .Where(o => YearMonth.From(o.PlannedDate) == viewed)
            .Select(o => new PlannedRow(
                incomesById[o.RecurringIncomeId], YearMonth.From(o.NominalMonth), o.PlannedDate, o.PlannedAmount, o.PlannedAccountId, o));

        var planned = computed
            .Concat(storedRows)
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Income.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var foreign = planned
            .Where(p => p.Stored?.Status != OccurrenceStatus.Settled)
            .Select(p => accounts[p.PlannedAccountId].Currency)
            .Where(c => c != CashPlaceBalances.BaseCurrency)
            .Distinct()
            .ToList();

        var today = WarsawCalendar.Today(timeProvider);
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (foreign.Count > 0)
        {
            var lookup = await quoteLookup.GetRatesAsync(foreign, today, cancellationToken);
            if (lookup.Status == InstrumentQuoteLookupStatus.Found)
            {
                foreach (var (currency, rate) in lookup.Rates!)
                {
                    rates[currency] = rate;
                }
            }
        }

        var occurrences = planned
            .Select(p =>
            {
                var currency = accounts[p.PlannedAccountId].Currency;
                var settlement = p.Stored is { Status: OccurrenceStatus.Settled } settled ? settled : null;
                var account = accounts[settlement?.AccountId ?? p.PlannedAccountId];
                var estimated = settlement is null && currency != CashPlaceBalances.BaseCurrency;

                decimal? amountPln;
                if (settlement is not null)
                {
                    amountPln = Round(p.Amount * settlement.FxRateToPln!.Value);
                }
                else if (!estimated)
                {
                    amountPln = p.Amount;
                }
                else
                {
                    amountPln = rates.TryGetValue(currency, out var rate) ? Round(p.Amount * rate) : null;
                }

                return new IncomeOccurrenceResponse
                {
                    RecurringIncomeId = p.Income.Id,
                    NominalMonth = p.Nominal.ToString(),
                    Date = p.Date,
                    Name = p.Income.Name,
                    AccountId = account.Id,
                    AccountName = account.Name,
                    AccountArchived = account.IsArchived,
                    Amount = p.Amount,
                    Currency = currency,
                    AmountPln = amountPln,
                    Estimated = estimated,
                    Status = p.Stored?.Status ?? OccurrenceStatus.Pending,
                    OutOfRule = p.Stored is not null && p.Income.ComputedAt(p.Nominal)?.Date != p.Date,
                    PlannedAccountId = p.PlannedAccountId,
                    ActualAmount = settlement?.ActualAmount,
                    ActualDate = settlement?.ActualDate,
                    Difference = p.Stored is null ? null : (settlement?.ActualAmount ?? 0m) - p.Amount,
                    ActualAmountPln = settlement is null ? null : Round(settlement.ActualAmount!.Value * settlement.FxRateToPln!.Value),
                    DepositCreated = settlement?.TransactionId is not null,
                    Overdue = (p.Stored?.Status ?? OccurrenceStatus.Pending) == OccurrenceStatus.Pending
                        && p.Date < today && p.Date >= p.Income.CreatedOn
                };
            })
            .ToList();

        var definitions = incomes
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

        var balance = new IncomeBalanceResponse
        {
            PlanPln = occurrences.Sum(o => o.AmountPln ?? 0m),
            ActualPln = occurrences.Sum(o => o.ActualAmountPln ?? 0m),
            PendingPln = occurrences.Where(o => o.Status == OccurrenceStatus.Pending).Sum(o => o.AmountPln ?? 0m),
            DifferencePln = occurrences
                .Where(o => o.Status != OccurrenceStatus.Pending)
                .Sum(o => (o.ActualAmountPln ?? 0m) - (o.AmountPln ?? 0m))
        };
        var fxUnavailable = foreign.Any(c => !rates.ContainsKey(c));

        var expenses = await BuildExpensesAsync(viewed, cancellationToken);

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
                ForecastPln = balance.ActualPln + balance.PendingPln - expenses.CostPln,
                Incomplete = fxUnavailable
            }
        };
    }

    private async Task<PlanningExpensesResponse> BuildExpensesAsync(YearMonth viewed, CancellationToken cancellationToken)
    {
        var expenses = await dbContext.RecurringExpenses
            .AsNoTracking()
            .Include(e => e.Versions)
            .ToListAsync(cancellationToken);

        var plans = expenses.Select(e => (Expense: e, Plan: e.ToReservePlan())).ToList();

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
                    NextPayment = NextPayment(viewed, p.Plan)
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

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record PlannedRow(
        RecurringIncome Income,
        YearMonth Nominal,
        DateOnly Date,
        decimal Amount,
        Guid PlannedAccountId,
        RecurringIncomeOccurrence? Stored);
}
