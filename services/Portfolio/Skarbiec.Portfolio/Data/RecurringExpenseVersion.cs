using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class RecurringExpenseVersion : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid RecurringExpenseId { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required decimal Amount { get; set; }
    public required int IntervalMonths { get; set; }
    public required RecurrenceDayKind DayKind { get; set; }
    public int? DayOfMonth { get; set; }
    public required BusinessDayShift Shift { get; set; }
}
