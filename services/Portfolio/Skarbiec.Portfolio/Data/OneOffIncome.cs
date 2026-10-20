using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class OneOffIncome : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required decimal Amount { get; set; }
    public required Guid AccountId { get; set; }
    public required DateOnly Date { get; set; }
}
