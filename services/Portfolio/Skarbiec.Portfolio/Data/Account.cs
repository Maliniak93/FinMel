using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class Account : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required AccountType Type { get; init; }
    public required TaxWrapper Wrapper { get; init; }
    public required string Currency { get; init; }
    public bool IsArchived { get; set; }
    public Guid? DefaultGroupId { get; set; }
}
