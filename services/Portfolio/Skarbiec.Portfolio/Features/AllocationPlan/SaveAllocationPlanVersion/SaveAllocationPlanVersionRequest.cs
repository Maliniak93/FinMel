using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Features.Planning.Allocation;

namespace Skarbiec.Portfolio.Features.AllocationPlan.SaveAllocationPlanVersion;

public sealed record SaveAllocationPlanVersionRequest : IValidatableObject
{
    private const decimal MaxFixedShare = 1_000_000m;

    public required IReadOnlyList<AllocationPlanRowRequest> Buckets { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bucketIds = new HashSet<Guid>();

        foreach (var row in Buckets)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > AllocationPlanRowRequest.MaxNameLength)
            {
                yield return new ValidationResult($"A name of 1 to {AllocationPlanRowRequest.MaxNameLength} characters is required.", [nameof(row.Name)]);
            }
            else if (!names.Add(name))
            {
                yield return new ValidationResult($"The name '{name}' is used twice.", [nameof(row.Name)]);
            }

            if (row.BucketId is { } bucketId && !bucketIds.Add(bucketId))
            {
                yield return new ValidationResult($"The bucket '{bucketId}' is used twice.", [nameof(row.BucketId)]);
            }

            if (!Enum.IsDefined(row.Kind))
            {
                yield return new ValidationResult("Unknown kind.", [nameof(row.Kind)]);
            }

            if (!Enum.IsDefined(row.ShareType))
            {
                yield return new ValidationResult("Unknown share type.", [nameof(row.ShareType)]);
            }
            else if (row.ShareType == BucketShareType.Fixed && row.Share is < 0.01m or > MaxFixedShare)
            {
                yield return new ValidationResult($"A fixed share is between 0.01 and {MaxFixedShare:0}.", [nameof(row.Share)]);
            }
            else if (row.ShareType == BucketShareType.Percent && (row.Share < (row.IsRemainder ? 0m : 0.01m) || row.Share > 100m))
            {
                yield return new ValidationResult("A percent share is between 0.01 and 100 (the remainder may be 0).", [nameof(row.Share)]);
            }
        }

        if (Buckets.Count > 0 && Buckets.Count(b => b.IsRemainder) != 1)
        {
            yield return new ValidationResult("A plan needs exactly one remainder bucket.", [nameof(Buckets)]);
        }
    }
}

public sealed record AllocationPlanRowRequest
{
    internal const int MaxNameLength = 100;

    /// <summary>Set to keep an existing bucket; absent creates a new one.</summary>
    public Guid? BucketId { get; init; }

    public required string Name { get; init; }
    public required BucketKind Kind { get; init; }
    public required BucketShareType ShareType { get; init; }

    /// <summary>PLN for a Fixed bucket, percent for a Percent bucket.</summary>
    public required decimal Share { get; init; }

    public required bool IsRemainder { get; init; }
    public Guid? AccountId { get; init; }
}
