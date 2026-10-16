using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class AllocationBalanceOverride : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid BucketId { get; init; }

    /// <summary>The first day of the month the balance applies from.</summary>
    public required DateOnly Month { get; init; }

    /// <summary>The bucket's balance at the start of that month.</summary>
    public required decimal Amount { get; init; }
}
