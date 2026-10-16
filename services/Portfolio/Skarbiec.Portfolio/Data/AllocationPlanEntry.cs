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

    /// <summary>Null for a cushion, which takes the gap to its target instead of a share.</summary>
    public BucketShareType? ShareType { get; init; }

    /// <summary>PLN for a Fixed bucket, percent for a Percent bucket; null for a cushion.</summary>
    public decimal? Share { get; init; }

    /// <summary>Order within its share type's group.</summary>
    public required int Position { get; init; }

    public required bool IsRemainder { get; init; }
    public Guid? AccountId { get; init; }

    /// <summary>The cushion's target in months of the month's cost; null for any other bucket.</summary>
    public int? TargetMonths { get; init; }
}
