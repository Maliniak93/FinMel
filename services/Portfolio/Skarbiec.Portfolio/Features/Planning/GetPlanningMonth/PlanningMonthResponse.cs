using System.Text.Json.Serialization;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed record PlanningMonthResponse
{
    public required PlanningIncomesResponse Incomes { get; init; }
}

public sealed record PlanningIncomesResponse
{
    public required IReadOnlyList<IncomeOccurrenceResponse> Occurrences { get; init; }

    public required IncomeBalanceResponse Balance { get; init; }

    /// <summary>True when a needed FX rate is missing, so the balance is incomplete.</summary>
    public required bool FxUnavailable { get; init; }

    public required IReadOnlyList<IncomeDefinitionResponse> Definitions { get; init; }
}

public sealed record IncomeBalanceResponse
{
    /// <summary>PLN sum of every occurrence's plan whose PLN amount is known.</summary>
    public required decimal PlanPln { get; init; }

    /// <summary>PLN sum of the settled actual amounts at the frozen rate.</summary>
    public required decimal ActualPln { get; init; }

    /// <summary>PLN sum of the pending plans.</summary>
    public required decimal PendingPln { get; init; }

    /// <summary>Actual minus plan of the settled and skipped occurrences.</summary>
    public required decimal DifferencePln { get; init; }
}

public sealed record IncomeOccurrenceResponse
{
    public required Guid RecurringIncomeId { get; init; }

    /// <summary>The month the rule produced the date for (yyyy-MM); a business-day shift can move the date into a neighbouring month.</summary>
    public required string NominalMonth { get; init; }

    public required DateOnly Date { get; init; }
    public required string Name { get; init; }

    /// <summary>The settlement account once settled, the planned one otherwise.</summary>
    public required Guid AccountId { get; init; }

    public required string AccountName { get; init; }
    public required bool AccountArchived { get; init; }

    /// <summary>The planned amount, frozen once the occurrence is settled or skipped.</summary>
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    /// <summary>The planned amount in PLN: at the rate frozen on settlement, else the latest-rate estimate (null when the rate is missing).</summary>
    public required decimal? AmountPln { get; init; }

    public required bool Estimated { get; init; }
    public required OccurrenceStatus Status { get; init; }

    /// <summary>True when the income's current rule no longer generates this stored occurrence at its planned date.</summary>
    public required bool OutOfRule { get; init; }

    public required Guid PlannedAccountId { get; init; }
    public required decimal? ActualAmount { get; init; }
    public required DateOnly? ActualDate { get; init; }

    /// <summary>Actual minus planned in the currency; minus the plan when skipped, null while pending.</summary>
    public required decimal? Difference { get; init; }

    public required decimal? ActualAmountPln { get; init; }

    /// <summary>True when the settlement created a cash deposit on its account.</summary>
    public required bool DepositCreated { get; init; }

    /// <summary>True when the occurrence is pending, planned before today and not before the income was created.</summary>
    public required bool Overdue { get; init; }
}

public sealed record IncomeDefinitionResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly? EndDate { get; init; }
    public required RecurringIncomeStatus Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required bool AccountArchived { get; init; }
    public required int IntervalMonths { get; init; }
    public required RecurrenceDayKind DayKind { get; init; }
    public required int? DayOfMonth { get; init; }
    public required BusinessDayShift Shift { get; init; }

    /// <summary>The month (yyyy-MM) the values above took effect from.</summary>
    public required string EffectiveFrom { get; init; }

    /// <summary>The earliest scheduled change after the viewed month, null when there is none.</summary>
    public required IncomeNextChangeResponse? NextChange { get; init; }
}

public sealed record IncomeNextChangeResponse
{
    /// <summary>The month (yyyy-MM) the change takes effect from.</summary>
    public required string EffectiveFrom { get; init; }

    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string AccountName { get; init; }
    public required int IntervalMonths { get; init; }
    public required RecurrenceDayKind DayKind { get; init; }
    public required int? DayOfMonth { get; init; }
    public required BusinessDayShift Shift { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<RecurringIncomeStatus>))]
public enum RecurringIncomeStatus
{
    Upcoming,
    Active,
    Ended,
}
