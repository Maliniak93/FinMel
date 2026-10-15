using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of a recurring income or planning endpoint calls it directly.
internal static class PlanningApi
{
    public const string RecurringIncomesUri = "/api/portfolio/recurring-incomes";

    public const string AccountHasRecurringIncomeErrorCode = "Conflict.AccountHasRecurringIncome";

    public static readonly DateTimeOffset PlanningTodayUtc = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

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
        bool Estimated);

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

    public sealed record IncomesView(
        List<OccurrenceView> Occurrences, decimal TotalPln, bool FxUnavailable, List<DefinitionView> Definitions);

    public sealed record PlanningMonthView(IncomesView Incomes);

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

    public static bool Is<TEnum>(this JsonElement element, TEnum expected) where TEnum : struct, Enum =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString() == expected.ToString()
            : element.GetInt32() == Convert.ToInt32(expected, System.Globalization.CultureInfo.InvariantCulture);
}
