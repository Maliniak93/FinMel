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
    private readonly IReadOnlyList<RecurringExpenseOccurrence> settledPayments;
    private readonly HashSet<(Guid, DateOnly)> settledPaymentKeys;
    private readonly Dictionary<Guid, RecurringExpense> expensesById;

    public PlanningForecast(
        IReadOnlyList<RecurringIncome> incomes,
        IReadOnlyDictionary<Guid, Account> accounts,
        IReadOnlyList<RecurringIncomeOccurrence> stored,
        IReadOnlyList<RecurringExpense> expenses,
        IReadOnlyList<RecurringExpenseOccurrence> settledPayments,
        IReadOnlyList<OneOffIncome> oneOffIncomes,
        IReadOnlyList<OneOffExpense> oneOffExpenses,
        IReadOnlyList<AssetGroup> groups,
        YearMonth currentMonth)
    {
        OneOffIncomes = oneOffIncomes;
        OneOffExpenses = oneOffExpenses;
        this.incomes = incomes;
        this.accounts = accounts;
        this.stored = stored;
        storedKeys = stored.Select(o => (o.RecurringIncomeId, o.NominalMonth)).ToHashSet();
        incomesById = incomes.ToDictionary(i => i.Id);
        Plans = expenses.Select(e => (Expense: e, Plan: e.ToReservePlan())).ToList();
        this.settledPayments = settledPayments;
        settledPaymentKeys = settledPayments.Select(o => (o.RecurringExpenseId, o.NominalMonth)).ToHashSet();
        expensesById = expenses.ToDictionary(e => e.Id);
        Groups = groups;
        CurrentMonth = currentMonth;
    }

    public IReadOnlyList<(RecurringExpense Expense, ReservePlan Plan)> Plans { get; }

    public IReadOnlyList<AssetGroup> Groups { get; }

    public YearMonth CurrentMonth { get; }

    // A contribution applies from the current month on, open-ended; the groups of the past months carry none.
    public IReadOnlyList<AssetGroup> ContributingGroupsIn(YearMonth month) =>
        month < CurrentMonth
            ? []
            : [.. Groups.Where(g => g.MonthlyContributionPln > 0m).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<OneOffIncome> OneOffIncomes { get; }

    public IReadOnlyList<OneOffExpense> OneOffExpenses { get; }

    public IReadOnlyList<string> ForeignIn(YearMonth month) =>
        PlannedIn(month)
            .Where(p => p.Stored?.Status != OccurrenceStatus.Settled)
            .Select(p => accounts[p.PlannedAccountId].Currency)
            .Concat(OneOffIncomes.Where(i => !i.IsSettled && YearMonth.From(i.Date) == month).Select(i => accounts[i.AccountId].Currency))
            .Where(c => c != CashPlaceBalances.BaseCurrency)
            .Distinct()
            .ToList();

    public IReadOnlyList<OneOffIncomeResponse> OneOffIncomesIn(
        YearMonth month, IReadOnlyDictionary<string, decimal> rates, DateOnly today) =>
        OneOffIncomes
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i =>
            {
                var account = accounts[i.ActualAccountId ?? i.AccountId];
                var currency = accounts[i.AccountId].Currency;
                var estimated = !i.IsSettled && currency != CashPlaceBalances.BaseCurrency;
                decimal? amountPln = i.IsSettled
                    ? Round(i.Amount * i.FxRateToPln!.Value)
                    : !estimated
                        ? i.Amount
                        : rates.TryGetValue(currency, out var rate) ? Round(i.Amount * rate) : null;

                return new OneOffIncomeResponse
                {
                    Id = i.Id,
                    Name = i.Name,
                    Date = i.Date,
                    AccountId = account.Id,
                    AccountName = account.Name,
                    AccountArchived = account.IsArchived,
                    Amount = i.Amount,
                    Currency = currency,
                    AmountPln = amountPln,
                    Estimated = estimated,
                    InMonth = YearMonth.From(i.Date) == month,
                    Status = i.IsSettled ? OccurrenceStatus.Settled : OccurrenceStatus.Pending,
                    ActualAmount = i.ActualAmount,
                    ActualDate = i.ActualDate,
                    ActualAmountPln = i.IsSettled ? Round(i.ActualAmount!.Value * i.FxRateToPln!.Value) : null,
                    Difference = i.IsSettled ? i.ActualAmount - i.Amount : null,
                    DepositCreated = i.TransactionId is not null,
                    Overdue = !i.IsSettled && i.Date < today
                };
            })
            .ToList();

    public Account? AccountOf(Guid? accountId) => accounts.GetValueOrDefault(accountId ?? Guid.Empty);

    public IReadOnlyList<ExpensePaymentResponse> ExpensePaymentsIn(YearMonth month)
    {
        var pending = Plans.SelectMany(p => ReserveSchedule.PaymentsIn(month, p.Plan)
            .Where(payment => !settledPaymentKeys.Contains((p.Expense.Id, payment.NominalMonth.FirstDay)))
            .Select(payment => PendingPayment(p.Expense, payment)));

        var settled = settledPayments
            .Where(o => YearMonth.From(o.PlannedDate) == month)
            .Select(SettledPayment);

        return pending
            .Concat(settled)
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsPaymentSettled(Guid recurringExpenseId, YearMonth nominalMonth) =>
        settledPaymentKeys.Contains((recurringExpenseId, nominalMonth.FirstDay));

    public IReadOnlyList<ExpensePaymentResponse> OverduePayments(DateOnly today)
    {
        if (Plans.Count == 0)
        {
            return [];
        }

        var last = YearMonth.From(today);
        var overdue = new List<ExpensePaymentResponse>();
        var seen = new HashSet<(Guid, string)>();
        for (var month = Plans.Min(p => YearMonth.From(p.Plan.Start)); month <= last; month = month.AddMonths(1))
        {
            foreach (var payment in ExpensePaymentsIn(month).Where(p => p.Status == OccurrenceStatus.Pending && p.Date < today))
            {
                if (seen.Add((payment.RecurringExpenseId, payment.NominalMonth)))
                {
                    overdue.Add(payment);
                }
            }
        }

        return overdue;
    }

    public IReadOnlyList<OneOffExpenseResponse> OverdueOneOffs(DateOnly today) =>
        OneOffExpenses
            .Where(e => !e.IsSettled && e.Date < today)
            .Select(e => YearMonth.From(e.Date))
            .Distinct()
            .SelectMany(month => OneOffExpensesIn(month)
                .Where(e => e.InMonth && e.Status == OccurrenceStatus.Pending && e.Date < today))
            .ToList();

    public IReadOnlyList<OneOffExpenseResponse> OneOffExpensesIn(YearMonth month) =>
        OneOffExpenses
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e =>
            {
                var account = accounts.GetValueOrDefault(e.ActualAccountId ?? e.AccountId ?? Guid.Empty);
                decimal? actualAmountPln = e.IsSettled ? SettledCost(e) : null;

                return new OneOffExpenseResponse
                {
                    Id = e.Id,
                    Name = e.Name,
                    Date = e.Date,
                    Amount = e.Amount,
                    InMonth = YearMonth.From(e.Date) == month,
                    AccountId = account?.Id,
                    AccountName = account?.Name,
                    Status = e.IsSettled ? OccurrenceStatus.Settled : OccurrenceStatus.Pending,
                    Currency = e.IsSettled ? account?.Currency : null,
                    ActualAmount = e.ActualAmount,
                    ActualDate = e.ActualDate,
                    ActualAmountPln = actualAmountPln,
                    Difference = actualAmountPln - e.Amount,
                    WithdrawalCreated = e.TransactionId is not null
                };
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
        var inMonth = oneOffs.Where(o => o.InMonth).ToList();
        var settledOneOffs = inMonth.Where(o => o.Status == OccurrenceStatus.Settled).ToList();

        return new IncomeBalanceResponse
        {
            PlanPln = occurrences.Sum(o => o.AmountPln ?? 0m) + inMonth.Sum(o => o.AmountPln ?? 0m),
            ActualPln = occurrences.Sum(o => o.ActualAmountPln ?? 0m) + settledOneOffs.Sum(o => o.ActualAmountPln ?? 0m),
            PendingPln = occurrences.Where(o => o.Status == OccurrenceStatus.Pending).Sum(o => o.AmountPln ?? 0m)
                + inMonth.Where(o => o.Status == OccurrenceStatus.Pending).Sum(o => o.AmountPln ?? 0m),
            DifferencePln = occurrences
                .Where(o => o.Status != OccurrenceStatus.Pending)
                .Sum(o => (o.ActualAmountPln ?? 0m) - (o.AmountPln ?? 0m))
                + settledOneOffs.Sum(o => (o.ActualAmountPln ?? 0m) - (o.AmountPln ?? 0m))
        };
    }

    public decimal CostIn(YearMonth month) =>
        Plans.Sum(p => ReserveSchedule.CostIn(month, p.Plan))
        + settledPayments.Where(o => YearMonth.From(o.PlannedDate) == month).Sum(o => SettledCost(o) - o.PlannedAmount)
        + OneOffExpenses.Where(e => YearMonth.From(e.Date) == month).Sum(e => e.IsSettled ? SettledCost(e) : e.Amount)
        + ContributingGroupsIn(month).Sum(g => g.MonthlyContributionPln);

    public MonthFigures FiguresIn(YearMonth month, IReadOnlyDictionary<string, decimal> rates, DateOnly today)
    {
        var balance = BalanceOf(OccurrencesIn(month, rates, today), OneOffIncomesIn(month, rates, today));
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

    private ExpensePaymentResponse PendingPayment(RecurringExpense expense, ReservePayment payment)
    {
        var plannedAccountId = expense.InForce(payment.NominalMonth).AccountId;
        var account = accounts.GetValueOrDefault(plannedAccountId ?? Guid.Empty);

        return new ExpensePaymentResponse
        {
            RecurringExpenseId = expense.Id,
            NominalMonth = payment.NominalMonth.ToString(),
            Date = payment.Date,
            Name = expense.Name,
            Amount = payment.Amount,
            Status = OccurrenceStatus.Pending,
            AccountId = account?.Id,
            AccountName = account?.Name,
            PlannedAccountId = plannedAccountId,
            Currency = null,
            ActualAmount = null,
            ActualDate = null,
            ActualAmountPln = null,
            Difference = null,
            WithdrawalCreated = false
        };
    }

    private ExpensePaymentResponse SettledPayment(RecurringExpenseOccurrence occurrence)
    {
        var expense = expensesById[occurrence.RecurringExpenseId];
        var nominal = YearMonth.From(occurrence.NominalMonth);
        var account = accounts[occurrence.AccountId];
        var actualAmountPln = SettledCost(occurrence);

        return new ExpensePaymentResponse
        {
            RecurringExpenseId = expense.Id,
            NominalMonth = nominal.ToString(),
            Date = occurrence.PlannedDate,
            Name = expense.Name,
            Amount = occurrence.PlannedAmount,
            Status = OccurrenceStatus.Settled,
            AccountId = account.Id,
            AccountName = account.Name,
            PlannedAccountId = expense.InForce(nominal).AccountId,
            Currency = account.Currency,
            ActualAmount = occurrence.ActualAmount,
            ActualDate = occurrence.ActualDate,
            ActualAmountPln = actualAmountPln,
            Difference = actualAmountPln - occurrence.PlannedAmount,
            WithdrawalCreated = occurrence.TransactionId is not null
        };
    }

    private static decimal SettledCost(RecurringExpenseOccurrence occurrence) => Round(occurrence.ActualAmount * occurrence.FxRateToPln);

    private static decimal SettledCost(OneOffExpense expense) => Round(expense.ActualAmount!.Value * expense.FxRateToPln!.Value);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record PlannedRow(
        RecurringIncome Income,
        YearMonth Nominal,
        DateOnly Date,
        decimal Amount,
        Guid PlannedAccountId,
        RecurringIncomeOccurrence? Stored);
}
