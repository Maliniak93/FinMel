using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of a recurring income or planning endpoint calls it directly.
internal static class PlanningApi
{
    public const string RecurringIncomesUri = "/api/portfolio/recurring-incomes";

    public const string AccountHasRecurringIncomeErrorCode = "Conflict.AccountHasRecurringIncome";

    public const string AccountHasIncomeOccurrencesErrorCode = "Conflict.AccountHasIncomeOccurrences";

    public const string RecurringIncomeHasSettlementsErrorCode = "Conflict.RecurringIncomeHasSettlements";

    public const string OccurrenceSettledErrorCode = "Conflict.OccurrenceSettled";

    public const string CurrencyMismatchErrorCode = "Validation.CurrencyMismatch";

    public static readonly DateTimeOffset PlanningTodayUtc = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset CreationDayUtc = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset AfterDueDayUtc = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    public sealed record OccurrenceView(
        Guid RecurringIncomeId,
        string NominalMonth,
        DateOnly Date,
        string Name,
        Guid AccountId,
        string AccountName,
        bool AccountArchived,
        decimal Amount,
        string Currency,
        decimal? AmountPln,
        bool Estimated,
        string Status,
        bool OutOfRule,
        Guid PlannedAccountId,
        decimal? ActualAmount,
        DateOnly? ActualDate,
        decimal? Difference,
        decimal? ActualAmountPln,
        bool DepositCreated,
        bool Overdue);

    public sealed record DefinitionView(
        Guid Id,
        string Name,
        DateOnly StartDate,
        DateOnly? EndDate,
        string Status,
        decimal Amount,
        string Currency,
        Guid AccountId,
        string AccountName,
        bool AccountArchived,
        int IntervalMonths,
        JsonElement DayKind,
        int? DayOfMonth,
        JsonElement Shift,
        string EffectiveFrom,
        NextChangeView? NextChange);

    public sealed record NextChangeView(
        string EffectiveFrom,
        decimal Amount,
        string Currency,
        string AccountName,
        int IntervalMonths,
        JsonElement DayKind,
        int? DayOfMonth,
        JsonElement Shift);

    public sealed record BalanceView(decimal PlanPln, decimal ActualPln, decimal PendingPln, decimal DifferencePln);

    public sealed record IncomesView(
        List<OccurrenceView> Occurrences, BalanceView Balance, bool FxUnavailable, List<DefinitionView> Definitions);

    public sealed record ExpensePaymentView(
        Guid RecurringExpenseId, string NominalMonth, DateOnly Date, string Name, decimal Amount);

    public sealed record NextPaymentView(DateOnly Date, decimal Amount);

    public sealed record ExpenseDefinitionView(
        Guid Id,
        string Name,
        DateOnly StartDate,
        DateOnly? EndDate,
        string Status,
        decimal Amount,
        int IntervalMonths,
        JsonElement DayKind,
        int? DayOfMonth,
        JsonElement Shift,
        decimal ReservePln,
        NextPaymentView? NextPayment,
        string EffectiveFrom,
        ExpenseNextChangeView? NextChange);

    public sealed record ExpenseNextChangeView(
        string EffectiveFrom,
        decimal Amount,
        int IntervalMonths,
        JsonElement DayKind,
        int? DayOfMonth,
        JsonElement Shift);

    public sealed record ExpensesView(
        List<ExpensePaymentView> Payments, decimal CostPln, List<ExpenseDefinitionView> Definitions);

    public sealed record FreeFundsView(decimal PlanPln, decimal ForecastPln, bool Incomplete);

    public sealed record AllocationBucketView(
        Guid BucketId,
        string Name,
        int Kind,
        int ShareType,
        decimal Share,
        int Position,
        bool IsRemainder,
        Guid? AccountId,
        string? AccountName,
        decimal AllocatedPln,
        bool Underfunded);

    public sealed record AllocationView(
        string? EffectiveFrom,
        string? NextChange,
        decimal ForecastPln,
        bool Incomplete,
        decimal DeficitPln,
        List<AllocationBucketView> Buckets,
        decimal ToInvestPln);

    public sealed record PlanningMonthView(
        IncomesView Incomes, ExpensesView Expenses, FreeFundsView FreeFunds, AllocationView Allocation);

    public const int InvestmentsKind = (int)BucketKind.Investments;

    public const int SavingsKind = (int)BucketKind.Savings;

    public const int FixedShare = (int)BucketShareType.Fixed;

    public const int PercentShare = (int)BucketShareType.Percent;

    public const string AllocationBucketNotFoundErrorCode = "NotFound.AllocationBucket";

    public const string AllocationPlanVersionNotFoundErrorCode = "NotFound.AllocationPlanVersion";

    public const string PercentSumOver100ErrorCode = "Validation.PercentSumOver100";

    public static string AllocationPlanVersionUri(string month) => $"/api/portfolio/allocation-plan/versions/{month}";

    public static object AllocationRow(
        string name,
        int kind,
        int shareType,
        decimal share,
        bool isRemainder = false,
        Guid? bucketId = null,
        Guid? accountId = null) => new { bucketId, name, kind, shareType, share, isRemainder, accountId };

    public static object NewAllocationPlanBody(params object[] buckets) => new { buckets };

    public static object[] FirstAllocationPlan(Guid? accountId = null) =>
    [
        AllocationRow("Wakacje", SavingsKind, FixedShare, 500m, accountId: accountId),
        AllocationRow("Akcje", InvestmentsKind, PercentShare, 50m),
        AllocationRow("Lokata", SavingsKind, PercentShare, 20m),
        AllocationRow("ETF", InvestmentsKind, PercentShare, 30m, isRemainder: true)
    ];

    // A PLN account with the "Pensja" income: the forecast is 12 243.27 every month.
    public static async Task<BankAccountsApi.BankAccount> CreateAllocationSetupAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 12_243.27m, dayOfMonth: 10);

        return account;
    }

    public static async Task SaveAllocationPlanAsync(
        this HttpClient client, string month, CancellationToken cancellationToken, params object[] buckets)
    {
        var response = await client.PutAsJsonAsync(AllocationPlanVersionUri(month), NewAllocationPlanBody(buckets), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task DeleteAllocationPlanVersionAsync(
        this HttpClient client, string month, CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(AllocationPlanVersionUri(month), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<AllocationView> GetAllocationAsync(
        this HttpClient client, string month, CancellationToken cancellationToken) =>
        (await client.GetPlanningMonthAsync(month, cancellationToken)).Allocation;

    public const string RecurringExpensesUri = "/api/portfolio/recurring-expenses";

    public static string RecurringExpenseUri(Guid id) => $"{RecurringExpensesUri}/{id}";

    public static string EndRecurringExpenseUri(Guid id) => $"{RecurringExpenseUri(id)}/end";

    public static object NewRecurringExpenseBody(
        string name = "Rachunki",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None) => new
        {
            name,
            amount,
            startDate = startDate ?? new DateOnly(2026, 1, 1),
            endDate,
            intervalMonths,
            dayKind = (int)dayKind,
            dayOfMonth = dayKind == RecurrenceDayKind.LastBusinessDay ? null : dayOfMonth,
            shift = (int)shift
        };

    public static async Task<Guid> CreateRecurringExpenseAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        string name = "Rachunki",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None)
    {
        var response = await client.PostAsJsonAsync(
            RecurringExpensesUri,
            NewRecurringExpenseBody(name, amount, startDate, endDate, intervalMonths, dayKind, dayOfMonth, shift),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    public static async Task EndRecurringExpenseAsync(
        this HttpClient client, Guid id, DateOnly endDate, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(EndRecurringExpenseUri(id), new { endDate }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task DeleteRecurringExpenseAsync(
        this HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(RecurringExpenseUri(id), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static string RecurringExpenseChangeUri(Guid id, string month) => $"{RecurringExpenseUri(id)}/changes/{month}";

    public static object UpdateRecurringExpenseBody(
        string effectiveFrom,
        string name = "Rachunki",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None) => new
        {
            name,
            startDate = startDate ?? new DateOnly(2026, 1, 1),
            endDate,
            effectiveFrom,
            amount,
            intervalMonths,
            dayKind = (int)dayKind,
            dayOfMonth = dayKind == RecurrenceDayKind.LastBusinessDay ? null : dayOfMonth,
            shift = (int)shift
        };

    public static async Task UpdateRecurringExpenseAsync(
        this HttpClient client,
        Guid id,
        string effectiveFrom,
        CancellationToken cancellationToken,
        string name = "Rachunki",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None)
    {
        var response = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody(effectiveFrom, name, amount, startDate, endDate, intervalMonths, dayKind, dayOfMonth, shift),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task DeleteRecurringExpenseChangeAsync(
        this HttpClient client, Guid id, string month, CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(RecurringExpenseChangeUri(id, month), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // "Życie" 3 000 on day 1 and "Rachunki" 1 000 on day 10, monthly from 2026-01-01.
    public static async Task CreateLivingExpensesAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        await client.CreateRecurringExpenseAsync(cancellationToken, "Życie", 3_000m, dayOfMonth: 1);
        await client.CreateRecurringExpenseAsync(cancellationToken, "Rachunki", 1_000m, dayOfMonth: 10);
    }

    public static readonly DateOnly OcToyotaStart = new(2027, 3, 15);

    // "OC Toyota" 1 200 every 12 months on day 15, starting 2027-03-15.
    public static Task<Guid> CreateOcToyotaAsync(this HttpClient client, CancellationToken cancellationToken) =>
        client.CreateRecurringExpenseAsync(
            cancellationToken, "OC Toyota", 1_200m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15);

    public sealed record HouseholdIncomes(
        BankAccountsApi.BankAccount Account, Guid Dominik, Guid Zona, Guid Plus800);

    // "Wypłata Dominik" 9 200 (last business day), "Wypłata żona" 6 500 (day 10, previous business day), "800+" 800 (day 10), all from 2026-01-01.
    public static async Task<HouseholdIncomes> CreateHouseholdIncomesAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var dominik = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);
        var zona = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata żona", 6_500m, new DateOnly(2026, 1, 1),
            dayOfMonth: 10, shift: BusinessDayShift.PreviousBusinessDay);
        var plus800 = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "800+", 800m, new DateOnly(2026, 1, 1), dayOfMonth: 10);

        return new HouseholdIncomes(account, dominik, zona, plus800);
    }

    public sealed record OverdueItemView(
        Guid RecurringIncomeId,
        string NominalMonth,
        DateOnly Date,
        string Name,
        Guid AccountId,
        string AccountName,
        bool AccountArchived,
        decimal Amount,
        string Currency);

    public sealed record OverdueView(List<OverdueItemView> Items);

    public const string OverdueUri = "/api/portfolio/planning/overdue";

    public static async Task<OverdueView> GetOverdueOccurrencesAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(OverdueUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OverdueView>(cancellationToken))!;
    }

    public static string RecurringIncomeUri(Guid id) => $"{RecurringIncomesUri}/{id}";

    public static string EndRecurringIncomeUri(Guid id) => $"{RecurringIncomeUri(id)}/end";

    public const string FirstVersionErrorCode = "Conflict.FirstVersion";

    public static string RecurringIncomeChangeUri(Guid id, string month) => $"{RecurringIncomeUri(id)}/changes/{month}";

    public static object UpdateRecurringIncomeBody(
        Guid accountId,
        string effectiveFrom,
        string name = "Wypłata",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None) => new
        {
            name,
            startDate = startDate ?? new DateOnly(2026, 1, 1),
            endDate,
            effectiveFrom,
            amount,
            accountId,
            intervalMonths,
            dayKind = (int)dayKind,
            dayOfMonth = dayKind == RecurrenceDayKind.LastBusinessDay ? null : dayOfMonth,
            shift = (int)shift
        };

    public static async Task UpdateRecurringIncomeAsync(
        this HttpClient client,
        Guid id,
        Guid accountId,
        string effectiveFrom,
        CancellationToken cancellationToken,
        string name = "Wypłata",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None)
    {
        var response = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(accountId, effectiveFrom, name, amount, startDate, endDate, intervalMonths, dayKind, dayOfMonth, shift),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task DeleteRecurringIncomeChangeAsync(
        this HttpClient client, Guid id, string month, CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(RecurringIncomeChangeUri(id, month), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static OccurrenceView Occurrence(this IncomesView incomes, string nominalMonth) =>
        incomes.Occurrences.Single(o => o.NominalMonth == nominalMonth);

    public static string OccurrenceUri(Guid id, string month) => $"{RecurringIncomeUri(id)}/occurrences/{month}";

    public static string SettleOccurrenceUri(Guid id, string month) => $"{OccurrenceUri(id, month)}/settlement";

    public static string SkipOccurrenceUri(Guid id, string month) => $"{OccurrenceUri(id, month)}/skip";

    public static object SettleOccurrenceBody(
        Guid accountId, decimal actualAmount, DateOnly actualDate, bool createDeposit = false) =>
        new { actualAmount, actualDate, accountId, createDeposit };

    public static async Task SettleOccurrenceAsync(
        this HttpClient client,
        Guid id,
        string month,
        Guid accountId,
        decimal actualAmount,
        DateOnly actualDate,
        CancellationToken cancellationToken,
        bool createDeposit = false)
    {
        var response = await client.PutAsJsonAsync(
            SettleOccurrenceUri(id, month),
            SettleOccurrenceBody(accountId, actualAmount, actualDate, createDeposit),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static readonly DateOnly SeptemberLastDay = new(2026, 9, 30);

    public sealed record IncomeDeposit(BankAccountsApi.BankAccount Account, Guid IncomeId, Guid TransactionId);

    // Settles 2026-09 of "Wypłata Dominik" onto a fresh PLN "mBank ROR" with a deposit; the caller's clock must be past 2026-09-30.
    public static async Task<IncomeDeposit> CreateIncomeDepositAsync(
        this HttpClient client, CancellationToken cancellationToken, decimal actualAmount = 9_143.27m)
    {
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var incomeId = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);
        await client.SettleOccurrenceAsync(
            incomeId, "2026-09", account.AccountId, actualAmount, SeptemberLastDay, cancellationToken, createDeposit: true);
        var deposit = Assert.Single((await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items);

        return new IncomeDeposit(account, incomeId, deposit.Id);
    }

    public static async Task SkipOccurrenceAsync(
        this HttpClient client, Guid id, string month, CancellationToken cancellationToken)
    {
        var response = await client.PutAsync(SkipOccurrenceUri(id, month), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task UndoOccurrenceAsync(
        this HttpClient client, Guid id, string month, CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(OccurrenceUri(id, month), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static string PlanningMonthUri(string month) => $"/api/portfolio/planning/months/{month}";

    public static object NewRecurringIncomeBody(
        Guid accountId,
        string name = "Wypłata",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None) => new
        {
            name,
            amount,
            accountId,
            startDate = startDate ?? new DateOnly(2026, 1, 1),
            endDate,
            intervalMonths,
            dayKind = (int)dayKind,
            dayOfMonth = dayKind == RecurrenceDayKind.LastBusinessDay ? null : dayOfMonth,
            shift = (int)shift
        };

    public static async Task<Guid> CreateRecurringIncomeAsync(
        this HttpClient client,
        Guid accountId,
        CancellationToken cancellationToken,
        string name = "Wypłata",
        decimal amount = 1_000m,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int intervalMonths = 1,
        RecurrenceDayKind dayKind = RecurrenceDayKind.DayOfMonth,
        int? dayOfMonth = 10,
        BusinessDayShift shift = BusinessDayShift.None)
    {
        var response = await client.PostAsJsonAsync(
            RecurringIncomesUri,
            NewRecurringIncomeBody(accountId, name, amount, startDate, endDate, intervalMonths, dayKind, dayOfMonth, shift),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    public static async Task EndRecurringIncomeAsync(
        this HttpClient client, Guid id, DateOnly endDate, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(EndRecurringIncomeUri(id), new { endDate }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task DeleteRecurringIncomeAsync(
        this HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var response = await client.DeleteAsync(RecurringIncomeUri(id), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<PlanningMonthView> GetPlanningMonthAsync(
        this HttpClient client, string month, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(PlanningMonthUri(month), cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PlanningMonthView>(cancellationToken))!;
    }

    public static async Task<ExpenseDefinitionView> ExpenseDefinitionAsync(
        this HttpClient client, Guid id, string month, CancellationToken cancellationToken) =>
        Assert.Single(
            (await client.GetPlanningMonthAsync(month, cancellationToken)).Expenses.Definitions,
            definition => definition.Id == id);

    public static bool Is<TEnum>(this JsonElement element, TEnum expected) where TEnum : struct, Enum =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString() == expected.ToString()
            : element.GetInt32() == Convert.ToInt32(expected, System.Globalization.CultureInfo.InvariantCulture);
}
