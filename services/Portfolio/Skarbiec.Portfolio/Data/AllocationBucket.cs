using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class AllocationBucket : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required BucketKind Kind { get; init; }
}
