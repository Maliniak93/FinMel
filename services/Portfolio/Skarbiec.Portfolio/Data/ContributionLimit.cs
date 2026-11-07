using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class ContributionLimit : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }

    public required TaxWrapper Wrapper { get; init; }
    public required int Year { get; init; }
    public required decimal Amount { get; set; }
}
