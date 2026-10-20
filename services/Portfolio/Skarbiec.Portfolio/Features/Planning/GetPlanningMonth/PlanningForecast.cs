using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.CashPlaces;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Features.RecurringExpenses;
using Skarbiec.Portfolio.Features.RecurringIncomes;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal sealed record MonthFigures(decimal Forecast, decimal Cost);

internal sealed class PlanningForecast
{
    private readonly IReadOnlyList<RecurringIncome> incomes;
    private readonly IReadOnlyDictionary<Guid, Account> accounts;
    private readonly IReadOnlyList<RecurringIncomeOccurrence> stored;
    private readonly HashSet<(Guid, DateOnly)> storedKeys;
    private readonly Dictionary<Guid, RecurringIncome> incomesById;
    private readonly Dictionary<YearMonth, IReadOnlyList<PlannedRow>> plannedByMonth = [];

    public PlanningForecast(
        IReadOnlyList<RecurringIncome> incomes,
        IReadOnlyDictionary<Guid, Account> accounts,
        IReadOnlyList<RecurringIncomeOccurrence> stored,
        IReadOnlyList<RecurringExpense> expenses,
        IReadOnlyList<OneOffIncome> oneOffIncomes,
        IReadOnlyList<OneOffExpense> oneOffExpenses)
    {
        OneOffIncomes = oneOffIncomes;
        OneOffExpenses = oneOffExpenses;
        this.incomes = incomes;
        this.accounts = accounts;
        this.stored = stored;
        storedKeys = stored.Select(o => (o.RecurringIncomeId, o.NominalMonth)).ToHashSet();
        incomesById = incomes.ToDictionary(i => i.Id);
        Plans = expenses.Select(e => (Expense: e, Plan: e.ToReservePlan())).ToList();
    }

    public IReadOnlyList<(RecurringExpense Expense, ReservePlan Plan)> Plans { get; }

    public IReadOnlyList<OneOffIncome> OneOffIncomes { get; }

    public IReadOnlyList<OneOffExpense> OneOffExpenses { get; }

    public IReadOnlyList<string> ForeignIn(YearMonth month) =>
        PlannedIn(month)
            .Where(p => p.Stored?.Status != OccurrenceStatus.Settled)
            .Select(p => accounts[p.PlannedAccountId].Currency)
            .Concat(OneOffIncomes.Where(i => YearMonth.From(i.Date) == month).Select(i => accounts[i.AccountId].Currency))
            .Where(c => c != CashPlaceBalances.BaseCurrency)
            .Distinct()
            .ToList();

    public IReadOnlyList<OneOffIncomeResponse> OneOffIncomesIn(YearMonth month, IReadOnlyDictionary<string, decimal> rates) =>
        OneOffIncomes
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i =>
            {
                var account = accounts[i.AccountId];
                var estimated = account.Currency != CashPlaceBalances.BaseCurrency;
                decimal? amountPln = !estimated
                    ? i.Amount
                    : rates.TryGetValue(account.Currency, out var rate) ? Round(i.Amount * rate) : null;

                return new OneOffIncomeResponse
                {
                    Id = i.Id,
                    Name = i.Name,
                    Date = i.Date,
                    AccountId = account.Id,
                    AccountName = account.Name,
                    AccountArchived = account.IsArchived,
                    Amount = i.Amount,
                    Currency = account.Currency,
                    AmountPln = amountPln,
                    Estimated = estimated,
                    InMonth = YearMonth.From(i.Date) == month
                };
            })
            .ToList();

    public IReadOnlyList<OneOffExpenseResponse> OneOffExpensesIn(YearMonth month) =>
        OneOffExpenses
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new OneOffExpenseResponse
            {
                Id = e.Id,
                Name = e.Name,
                Date = e.Date,
                Amount = e.Amount,
                InMonth = YearMonth.From(e.Date) == month
            })
            .ToList();

    public IReadOnlyList<IncomeOccurrenceResponse> OccurrencesIn(
        YearMonth month, IReadOnlyDictionary<string, decimal> rates, DateOnly today) =>
        PlannedIn(month)
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

    public static IncomeBalanceResponse BalanceOf(
        IReadOnlyList<IncomeOccurrenceResponse> occurrences, IReadOnlyList<OneOffIncomeResponse> oneOffs)
    {
        var oneOffPln = oneOffs.Where(o => o.InMonth).Sum(o => o.AmountPln ?? 0m);

        return new IncomeBalanceResponse
        {
            PlanPln = occurrences.Sum(o => o.AmountPln ?? 0m) + oneOffPln,
            ActualPln = occurrences.Sum(o => o.ActualAmountPln ?? 0m),
            PendingPln = occurrences.Where(o => o.Status == OccurrenceStatus.Pending).Sum(o => o.AmountPln ?? 0m) + oneOffPln,
            DifferencePln = occurrences
                .Where(o => o.Status != OccurrenceStatus.Pending)
                .Sum(o => (o.ActualAmountPln ?? 0m) - (o.AmountPln ?? 0m))
        };
    }

    public decimal CostIn(YearMonth month) =>
        Plans.Sum(p => ReserveSchedule.CostIn(month, p.Plan))
        + OneOffExpenses.Where(e => YearMonth.From(e.Date) == month).Sum(e => e.Amount);

    public MonthFigures FiguresIn(YearMonth month, IReadOnlyDictionary<string, decimal> rates, DateOnly today)
    {
        var balance = BalanceOf(OccurrencesIn(month, rates, today), OneOffIncomesIn(month, rates));
        var cost = CostIn(month);

        return new MonthFigures(balance.ActualPln + balance.PendingPln - cost, cost);
    }

    private IReadOnlyList<PlannedRow> PlannedIn(YearMonth month)
    {
        if (plannedByMonth.TryGetValue(month, out var cached))
        {
            return cached;
        }

        var computed = incomes
            .SelectMany(income => RecurrenceSchedule
                .OccurrencesIn(month, income.StartDate, income.EndDate, nominal => income.InForce(nominal).ToRule())
                .Where(occurrence => !storedKeys.Contains((income.Id, occurrence.NominalMonth.FirstDay)))
                .Select(occurrence =>
                {
                    var version = income.InForce(occurrence.NominalMonth);
                    return new PlannedRow(income, occurrence.NominalMonth, occurrence.Date, version.Amount, version.AccountId, null);
                }));

        var storedRows = stored
            .Where(o => YearMonth.From(o.PlannedDate) == month)
            .Select(o => new PlannedRow(
                incomesById[o.RecurringIncomeId], YearMonth.From(o.NominalMonth), o.PlannedDate, o.PlannedAmount, o.PlannedAccountId, o));

        var planned = computed
            .Concat(storedRows)
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Income.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        plannedByMonth[month] = planned;

        return planned;
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
