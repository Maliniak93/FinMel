using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class AllocationPlanEntry : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid VersionId { get; init; }
    public required Guid BucketId { get; init; }
    public required string Name { get; init; }
    public required BucketShareType ShareType { get; init; }

    /// <summary>PLN for a Fixed bucket, percent for a Percent bucket.</summary>
    public required decimal Share { get; init; }

    /// <summary>Order within its share type's group.</summary>
    public required int Position { get; init; }

    public required bool IsRemainder { get; init; }
    public Guid? AccountId { get; init; }
}
