using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using StoredTransferPlan = Skarbiec.Portfolio.Data.TransferPlan;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal sealed record PlanningMonthData(
    DateOnly Today,
    IReadOnlyList<RecurringIncome> Incomes,
    Dictionary<Guid, Account> Accounts,
    PlanningForecast Forecast,
    List<AllocationPlanVersion> Versions,
    List<AllocationBalanceOverride> Overrides,
    Dictionary<Guid, BucketKind> Kinds,
    IReadOnlyDictionary<string, decimal> Rates,
    TargetAllocationReplica? TargetReplica,
    ValuationReplica? ValuationReplica,
    int MinimumTransfer,
    int PaymentLeadDays,
    YearMonth? PlanStart,
    IReadOnlyList<StoredTransferPlan> TransferPlans);
