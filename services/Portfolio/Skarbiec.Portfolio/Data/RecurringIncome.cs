using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class RecurringIncome : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public required DateOnly CreatedOn { get; init; }

    public List<RecurringIncomeVersion> Versions { get; init; } = [];
}
