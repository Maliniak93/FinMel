using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class RecurringExpense : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public required DateOnly CreatedOn { get; init; }

    public List<RecurringExpenseVersion> Versions { get; init; } = [];
}
