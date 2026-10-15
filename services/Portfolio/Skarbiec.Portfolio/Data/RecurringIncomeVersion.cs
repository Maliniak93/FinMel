using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class RecurringIncomeVersion : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid RecurringIncomeId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required decimal Amount { get; init; }
    public required Guid AccountId { get; init; }
    public required int IntervalMonths { get; init; }
    public required RecurrenceDayKind DayKind { get; init; }
    public int? DayOfMonth { get; init; }
    public required BusinessDayShift Shift { get; init; }
}
