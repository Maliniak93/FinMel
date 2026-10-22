using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal sealed record PlanningMonthData(
    DateOnly Today,
    IReadOnlyList<RecurringIncome> Incomes,
    Dictionary<Guid, Account> Accounts,
    PlanningForecast Forecast,
    IReadOnlyDictionary<string, decimal> Rates,
    int PaymentLeadDays);
