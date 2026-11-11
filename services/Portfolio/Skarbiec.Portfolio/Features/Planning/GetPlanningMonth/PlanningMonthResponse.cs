using System.Text.Json.Serialization;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed record PlanningMonthResponse
{
    public required PlanningIncomesResponse Incomes { get; init; }

    public required PlanningExpensesResponse Expenses { get; init; }

    public required FreeFundsResponse FreeFunds { get; init; }

    /// <summary>Null for a past month and for a user without a group.</summary>
    public required PlanningSplitResponse? Split { get; init; }

    /// <summary>The weekends and Polish holidays of the viewed month, each date once.</summary>
    public required IReadOnlyList<DateOnly> NonBusinessDays { get; init; }

    /// <summary>IKE and IKZE accounts whose year contributions plus pending planned income exceed the limit, for the viewed month's calendar year.</summary>
    public required IReadOnlyList<ContributionLimitWarningResponse> ContributionLimitWarnings { get; init; }
}

public sealed record ContributionLimitWarningResponse
{
    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required TaxWrapper Wrapper { get; init; }
    public required int Year { get; init; }
    public required decimal LimitPln { get; init; }

    /// <summary>The account's actual contributions in the year.</summary>
    public required decimal ContributedPln { get; init; }

    /// <summary>The PLN sum of the year's pending planned incomes into the account.</summary>
    public required decimal PlannedPln { get; init; }

    public required decimal ExceededByPln { get; init; }
}

public sealed record PlanningExpensesResponse
{
    public required IReadOnlyList<ExpensePaymentResponse> Payments { get; init; }

    /// <summary>PLN sum of every expense's cost for the month: payments plus the reserve set aside for later ones, one-offs and the group contributions.</summary>
    public required decimal CostPln { get; init; }

    /// <summary>Each group's monthly contribution, from the current month on; empty for a past month.</summary>
    public required IReadOnlyList<GroupContributionResponse> GroupContributions { get; init; }

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

    /// <summary>The settlement account once settled, the default one otherwise; null when none.</summary>
    public required Guid? AccountId { get; init; }

    public required string? AccountName { get; init; }
    public required OccurrenceStatus Status { get; init; }

    /// <summary>The currency of the settlement account; null while pending.</summary>
    public required string? Currency { get; init; }

    public required decimal? ActualAmount { get; init; }
    public required DateOnly? ActualDate { get; init; }

    /// <summary>The actual amount in PLN at the rate frozen on settlement; null while pending.</summary>
    public required decimal? ActualAmountPln { get; init; }

    /// <summary>Actual PLN minus planned, null while pending.</summary>
    public required decimal? Difference { get; init; }

    /// <summary>True when the settlement created a withdrawal on its account and it still exists.</summary>
    public required bool WithdrawalCreated { get; init; }
}

public sealed record ExpensePaymentResponse
{
    public required Guid RecurringExpenseId { get; init; }

    /// <summary>The month the rule produced the date for (yyyy-MM); a business-day shift can move the date into a neighbouring month.</summary>
    public required string NominalMonth { get; init; }

    public required DateOnly Date { get; init; }
    public required string Name { get; init; }

    /// <summary>The planned amount, frozen once the payment is settled.</summary>
    public required decimal Amount { get; init; }

    public required OccurrenceStatus Status { get; init; }

    /// <summary>The settlement account once settled, the definition's account otherwise; null when none.</summary>
    public required Guid? AccountId { get; init; }

    public required string? AccountName { get; init; }

    /// <summary>The account of the definition's version in force for the payment; null when none.</summary>
    public required Guid? PlannedAccountId { get; init; }

    /// <summary>The currency of the settlement account; null while pending.</summary>
    public required string? Currency { get; init; }

    public required decimal? ActualAmount { get; init; }
    public required DateOnly? ActualDate { get; init; }

    /// <summary>The actual amount in PLN at the rate frozen on settlement; null while pending.</summary>
    public required decimal? ActualAmountPln { get; init; }

    /// <summary>Actual PLN minus planned, null while pending.</summary>
    public required decimal? Difference { get; init; }

    /// <summary>True when the settlement created a withdrawal on its account and it still exists.</summary>
    public required bool WithdrawalCreated { get; init; }
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

    /// <summary>The account of the version in force; null when none.</summary>
    public required Guid? AccountId { get; init; }

    public required string? AccountName { get; init; }
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
    public required Guid? AccountId { get; init; }
    public required string? AccountName { get; init; }
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

    /// <summary>Settled income minus settled expenses and the funding of the groups; negative is a deficit.</summary>
    public required decimal ActualPln { get; init; }

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

    /// <summary>Every one-off income of the user, ordered by date; only those in the viewed month count towards the balance, a settled one stays in its planned month.</summary>
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

    /// <summary>The amount in PLN: at the rate frozen on settlement, else the latest-rate estimate (null when the rate is missing).</summary>
    public required decimal? AmountPln { get; init; }

    public required bool Estimated { get; init; }

    /// <summary>True when the date falls in the viewed month.</summary>
    public required bool InMonth { get; init; }

    public required OccurrenceStatus Status { get; init; }
    public required decimal? ActualAmount { get; init; }
    public required DateOnly? ActualDate { get; init; }
    public required decimal? ActualAmountPln { get; init; }

    /// <summary>Actual minus planned in the currency, null while pending.</summary>
    public required decimal? Difference { get; init; }

    /// <summary>True when the settlement created a cash deposit on its account.</summary>
    public required bool DepositCreated { get; init; }

    /// <summary>True when the income is pending and dated before today.</summary>
    public required bool Overdue { get; init; }
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

public sealed record GroupContributionResponse
{
    public required Guid GroupId { get; init; }
    public required string Name { get; init; }
    /// <summary>The sum of all the group's contributions.</summary>
    public required decimal AmountPln { get; init; }

    /// <summary>The part of the contributions the Plan counts: paid from an income account or without an account.</summary>
    public required decimal FromIncomePln { get; init; }

    public required IReadOnlyList<ContributionLineResponse> Contributions { get; init; }

    /// <summary>PLN that entered the group this month net of what left it, at least 0; null outside the current month.</summary>
    public required decimal? FundedPln { get; init; }

    /// <summary>Null outside the current month.</summary>
    public required GroupContributionStatus? Status { get; init; }
}

public sealed record ContributionLineResponse
{
    public required decimal AmountPln { get; init; }
    public required Guid? AccountId { get; init; }
    public required string? AccountName { get; init; }

    /// <summary>True when the Plan counts this contribution.</summary>
    public required bool FromIncome { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<GroupContributionStatus>))]
public enum GroupContributionStatus
{
    Unsettled,
    Partial,
    Settled,
}

/// <summary>How the viewed month's free funds would be divided between the groups; information only.</summary>
public sealed record PlanningSplitResponse
{
    public required decimal FreeFundsPln { get; init; }

    /// <summary>The missing amount when the free funds are 0 or less, in which case no amounts are given; null otherwise.</summary>
    public required decimal? DeficitPln { get; init; }

    /// <summary>Null when the user has no cushion group.</summary>
    public required SplitCushionResponse? Cushion { get; init; }

    public required IReadOnlyList<SplitGroupResponse> Groups { get; init; }

    /// <summary>What no regular group takes: the free funds the cushion leaves when there is no regular group.</summary>
    public required decimal UnassignedPln { get; init; }

    /// <summary>True when Reporting's valuation has not arrived yet: the cushion level counts as 0 and breakdowns follow percentages.</summary>
    public required bool ValuationMissing { get; init; }
}

public sealed record SplitCushionResponse
{
    public required Guid GroupId { get; init; }
    public required string Name { get; init; }

    /// <summary>The cushion's value now plus its contributions and allocations of the months before the viewed one.</summary>
    public required decimal LevelPln { get; init; }

    public required decimal TargetPln { get; init; }
    public required decimal MissingPln { get; init; }
    public required decimal AllocationPln { get; init; }
}

public sealed record SplitGroupResponse
{
    public required Guid GroupId { get; init; }
    public required string Name { get; init; }
    public required decimal SharePercent { get; init; }
    public required decimal ContributionPln { get; init; }
    public required decimal AllocationPln { get; init; }

    /// <summary>The contribution per asset class by today's values; null without a contribution or a class allocation.</summary>
    public required IReadOnlyList<SplitClassAmountResponse>? Breakdown { get; init; }
}

public sealed record SplitClassAmountResponse
{
    public required AssetClass AssetClass { get; init; }
    public required decimal AmountPln { get; init; }
}
