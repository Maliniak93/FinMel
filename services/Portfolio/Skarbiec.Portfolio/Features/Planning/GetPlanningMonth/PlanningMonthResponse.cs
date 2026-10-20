using System.Text.Json.Serialization;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.TransferPlan;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed record PlanningMonthResponse
{
    public required PlanningIncomesResponse Incomes { get; init; }

    public required PlanningExpensesResponse Expenses { get; init; }

    public required FreeFundsResponse FreeFunds { get; init; }

    public required AllocationResponse Allocation { get; init; }

    public required TransferPlanResponse TransferPlan { get; init; }

    /// <summary>The weekends and Polish holidays of the viewed month, each date once.</summary>
    public required IReadOnlyList<DateOnly> NonBusinessDays { get; init; }
}

public sealed record AllocationResponse
{
    /// <summary>The month (yyyy-MM) of the plan version in force, null when none is.</summary>
    public required string? EffectiveFrom { get; init; }

    /// <summary>The earliest scheduled plan version after the viewed month (yyyy-MM), null when there is none.</summary>
    public required string? NextChange { get; init; }

    /// <summary>The free-funds forecast the buckets split.</summary>
    public required decimal ForecastPln { get; init; }

    /// <summary>True when a needed FX rate is missing, so the forecast is incomplete.</summary>
    public required bool Incomplete { get; init; }

    /// <summary>The shortfall when the forecast is negative; 0 otherwise.</summary>
    public required decimal DeficitPln { get; init; }

    /// <summary>The fixed group first, then the percent group, each by position; empty when no version is in force.</summary>
    public required IReadOnlyList<AllocationBucketResponse> Buckets { get; init; }

    /// <summary>PLN sum of the Investments, IKE, IKZE and IKZE JDG buckets.</summary>
    public required decimal ToInvestPln { get; init; }

    /// <summary>The expense reserve held inside the month's cost, never split again; null when it is 0 and no account is set.</summary>
    public required ReserveResponse? Reserve { get; init; }
}

public sealed record ReserveResponse
{
    /// <summary>PLN set aside this month for expenses due every more than one month.</summary>
    public required decimal MonthPln { get; init; }

    /// <summary>PLN accumulated by the end of the month and not yet paid out.</summary>
    public required decimal BalancePln { get; init; }

    public required Guid? AccountId { get; init; }
    public required string? AccountName { get; init; }
}

public sealed record AllocationBucketResponse
{
    public required Guid BucketId { get; init; }
    public required string Name { get; init; }
    public required BucketKind Kind { get; init; }

    /// <summary>Null for a cushion.</summary>
    public required BucketShareType? ShareType { get; init; }

    /// <summary>PLN for a Fixed bucket, percent for a Percent bucket; null for a cushion.</summary>
    public required decimal? Share { get; init; }

    public required int Position { get; init; }
    public required bool IsRemainder { get; init; }
    public required Guid? AccountId { get; init; }
    public required string? AccountName { get; init; }
    public required decimal AllocatedPln { get; init; }

    /// <summary>True when a fixed bucket got less than its share.</summary>
    public required bool Underfunded { get; init; }

    /// <summary>The cushion's balance against its target, null for any other bucket.</summary>
    public required CushionResponse? Cushion { get; init; }

    /// <summary>True when the yearly limit cut the bucket's allocation short.</summary>
    public required bool LimitReached { get; init; }

    /// <summary>The year's contributions against the statutory limit, null for a bucket without one.</summary>
    public required LimitResponse? Limit { get; init; }

    /// <summary>Every balance adjustment of a cushion or limited bucket by month; null for any other bucket.</summary>
    public required IReadOnlyList<BalanceAdjustmentResponse>? Adjustments { get; init; }
}

public sealed record BalanceAdjustmentResponse
{
    /// <summary>The month (yyyy-MM) the balance applies from.</summary>
    public required string Month { get; init; }

    public required decimal AmountPln { get; init; }
}

public sealed record LimitResponse
{
    public required int Year { get; init; }
    public required decimal LimitPln { get; init; }

    /// <summary>Contributed in the year before the month.</summary>
    public required decimal ContributedStartPln { get; init; }

    /// <summary>Contributed in the year after the month's allocation.</summary>
    public required decimal ContributedEndPln { get; init; }

    /// <summary>True when the counter is within 1 zł of the limit or above it.</summary>
    public required bool Reached { get; init; }

    /// <summary>True when the year's limit is taken from the last known year.</summary>
    public required bool Estimated { get; init; }
}

public sealed record CushionResponse
{
    public required decimal BalanceStartPln { get; init; }

    /// <summary>Target months times the month's expense cost.</summary>
    public required decimal TargetPln { get; init; }

    public required int TargetMonths { get; init; }

    /// <summary>The balance after the month's allocation.</summary>
    public required decimal BalanceEndPln { get; init; }

    /// <summary>True when the balance is within 1 zł of the target or above it.</summary>
    public required bool Reached { get; init; }
}

public sealed record PlanningExpensesResponse
{
    public required IReadOnlyList<ExpensePaymentResponse> Payments { get; init; }

    /// <summary>PLN sum of every expense's cost for the month: payments plus the reserve set aside for later ones.</summary>
    public required decimal CostPln { get; init; }

    public required IReadOnlyList<ExpenseDefinitionResponse> Definitions { get; init; }

    /// <summary>Every one-off expense of the user, ordered by date; only those in the viewed month count towards the cost.</summary>
    public required IReadOnlyList<OneOffExpenseResponse> OneOffs { get; init; }
}

public sealed record OneOffExpenseResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Amount { get; init; }

    /// <summary>True when the date falls in the viewed month.</summary>
    public required bool InMonth { get; init; }
}

public sealed record ExpensePaymentResponse
{
    public required Guid RecurringExpenseId { get; init; }

    /// <summary>The month the rule produced the date for (yyyy-MM); a business-day shift can move the date into a neighbouring month.</summary>
    public required string NominalMonth { get; init; }

    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public required decimal Amount { get; init; }
}

public sealed record ExpenseDefinitionResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly? EndDate { get; init; }
    public required RecurringIncomeStatus Status { get; init; }
    public required decimal Amount { get; init; }
    public required int IntervalMonths { get; init; }
    public required RecurrenceDayKind DayKind { get; init; }
    public required int? DayOfMonth { get; init; }
    public required BusinessDayShift Shift { get; init; }

    /// <summary>The PLN set aside in the viewed month for this expense's payments.</summary>
    public required decimal ReservePln { get; init; }

    /// <summary>The first payment on or after the viewed month, null when none falls within the next 24 months.</summary>
    public required ExpenseNextPaymentResponse? NextPayment { get; init; }

    /// <summary>The month (yyyy-MM) the amount and rule above took effect from.</summary>
    public required string EffectiveFrom { get; init; }

    /// <summary>The earliest scheduled change after the viewed month, null when there is none.</summary>
    public required ExpenseNextChangeResponse? NextChange { get; init; }
}

public sealed record ExpenseNextChangeResponse
{
    /// <summary>The month (yyyy-MM) the change takes effect from.</summary>
    public required string EffectiveFrom { get; init; }

    public required decimal Amount { get; init; }
    public required int IntervalMonths { get; init; }
    public required RecurrenceDayKind DayKind { get; init; }
    public required int? DayOfMonth { get; init; }
    public required BusinessDayShift Shift { get; init; }
}

public sealed record ExpenseNextPaymentResponse
{
    public required DateOnly Date { get; init; }
    public required decimal Amount { get; init; }
}

public sealed record FreeFundsResponse
{
    /// <summary>Planned income minus the month's expense cost; negative is a deficit.</summary>
    public required decimal PlanPln { get; init; }

    /// <summary>Settled income plus pending income minus the month's expense cost; negative is a deficit.</summary>
    public required decimal ForecastPln { get; init; }

    /// <summary>True when a needed FX rate is missing, so the figures are incomplete.</summary>
    public required bool Incomplete { get; init; }
}

public sealed record PlanningIncomesResponse
{
    public required IReadOnlyList<IncomeOccurrenceResponse> Occurrences { get; init; }

    public required IncomeBalanceResponse Balance { get; init; }

    /// <summary>True when a needed FX rate is missing, so the balance is incomplete.</summary>
    public required bool FxUnavailable { get; init; }

    public required IReadOnlyList<IncomeDefinitionResponse> Definitions { get; init; }

    /// <summary>Every one-off income of the user, ordered by date; only those in the viewed month count towards the balance.</summary>
    public required IReadOnlyList<OneOffIncomeResponse> OneOffs { get; init; }
}

public sealed record OneOffIncomeResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateOnly Date { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required bool AccountArchived { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>The amount in PLN at the latest rate (null when the rate is missing).</summary>
    public required decimal? AmountPln { get; init; }

    public required bool Estimated { get; init; }

    /// <summary>True when the date falls in the viewed month.</summary>
    public required bool InMonth { get; init; }
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
