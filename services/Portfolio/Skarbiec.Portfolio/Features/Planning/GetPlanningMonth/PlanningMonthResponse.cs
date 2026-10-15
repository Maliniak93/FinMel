using System.Text.Json.Serialization;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed record PlanningMonthResponse
{
    public required PlanningIncomesResponse Incomes { get; init; }
}

public sealed record PlanningIncomesResponse
{
    public required IReadOnlyList<IncomeOccurrenceResponse> Occurrences { get; init; }

    /// <summary>PLN sum of the occurrences whose PLN amount is known.</summary>
    public required decimal TotalPln { get; init; }

    /// <summary>True when a needed FX rate is missing, so the total is incomplete.</summary>
    public required bool FxUnavailable { get; init; }

    public required IReadOnlyList<IncomeDefinitionResponse> Definitions { get; init; }
}

public sealed record IncomeOccurrenceResponse
{
    public required Guid RecurringIncomeId { get; init; }

    /// <summary>The month the rule produced the date for (yyyy-MM); a business-day shift can move the date into a neighbouring month.</summary>
    public required string NominalMonth { get; init; }

    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required bool AccountArchived { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>Equal to the amount for PLN; the latest-rate estimate otherwise, null when the rate is missing.</summary>
    public required decimal? AmountPln { get; init; }

    public required bool Estimated { get; init; }
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
