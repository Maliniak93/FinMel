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
            else if (row.Kind == BucketKind.Cushion)
            {
                foreach (var problem in ValidateCushion(row))
                {
                    yield return problem;
                }
            }
            else
            {
                foreach (var problem in ValidateShared(row))
                {
                    yield return problem;
                }
            }
        }

        if (Buckets.Count(b => b.Kind == BucketKind.Cushion) > 1)
        {
            yield return new ValidationResult("A plan holds at most one cushion.", [nameof(Buckets)]);
        }

        if (Buckets.Count > 0 && Buckets.Count(b => b.IsRemainder) != 1)
        {
            yield return new ValidationResult("A plan needs exactly one remainder bucket.", [nameof(Buckets)]);
        }
    }

    private static IEnumerable<ValidationResult> ValidateCushion(AllocationPlanRowRequest row)
    {
        if (row.ShareType is not null || row.Share is not null)
        {
            yield return new ValidationResult("A cushion has no share.", [nameof(row.Share)]);
        }

        if (row.IsRemainder)
        {
            yield return new ValidationResult("A cushion cannot be the remainder.", [nameof(row.IsRemainder)]);
        }

        if (row.TargetMonths is not (>= AllocationPlanRowRequest.MinTargetMonths and <= AllocationPlanRowRequest.MaxTargetMonths))
        {
            yield return new ValidationResult(
                $"A cushion target is {AllocationPlanRowRequest.MinTargetMonths} to {AllocationPlanRowRequest.MaxTargetMonths} months.",
                [nameof(row.TargetMonths)]);
        }

        if (row.StartingBalance is { } balance)
        {
            if (row.BucketId is not null)
            {
                yield return new ValidationResult("A starting balance is set only on a new cushion.", [nameof(row.StartingBalance)]);
            }
            else if (balance is < 0m or > AllocationPlanRowRequest.MaxStartingBalance)
            {
                yield return new ValidationResult(
                    $"A starting balance is between 0 and {AllocationPlanRowRequest.MaxStartingBalance:0}.", [nameof(row.StartingBalance)]);
            }
        }
    }

    private static IEnumerable<ValidationResult> ValidateShared(AllocationPlanRowRequest row)
    {
        if (row.TargetMonths is not null)
        {
            yield return new ValidationResult("Target months belong to a cushion only.", [nameof(row.TargetMonths)]);
        }

        if (row.StartingBalance is not null)
        {
            yield return new ValidationResult("A starting balance belongs to a cushion only.", [nameof(row.StartingBalance)]);
        }

        if (row.ShareType is not { } shareType || row.Share is not { } share)
        {
            yield return new ValidationResult("A share type and a share are required.", [nameof(row.Share)]);
        }
        else if (!Enum.IsDefined(shareType))
        {
            yield return new ValidationResult("Unknown share type.", [nameof(row.ShareType)]);
        }
        else if (shareType == BucketShareType.Fixed && share is < 0.01m or > MaxFixedShare)
        {
            yield return new ValidationResult($"A fixed share is between 0.01 and {MaxFixedShare:0}.", [nameof(row.Share)]);
        }
        else if (shareType == BucketShareType.Percent && (share < (row.IsRemainder ? 0m : 0.01m) || share > 100m))
        {
            yield return new ValidationResult("A percent share is between 0.01 and 100 (the remainder may be 0).", [nameof(row.Share)]);
        }
    }
}

public sealed record AllocationPlanRowRequest
{
    internal const int MaxNameLength = 100;
    internal const int MinTargetMonths = 1;
    internal const int MaxTargetMonths = 24;
    internal const decimal MaxStartingBalance = 1_000_000_000m;

    /// <summary>Set to keep an existing bucket; absent creates a new one.</summary>
    public Guid? BucketId { get; init; }

    public required string Name { get; init; }
    public required BucketKind Kind { get; init; }

    /// <summary>Required for every kind but a cushion, which has none.</summary>
    public BucketShareType? ShareType { get; init; }

    /// <summary>PLN for a Fixed bucket, percent for a Percent bucket; absent for a cushion.</summary>
    public decimal? Share { get; init; }

    public required bool IsRemainder { get; init; }
    public Guid? AccountId { get; init; }

    /// <summary>A cushion's target in months of the month's cost, 1 to 24.</summary>
    public int? TargetMonths { get; init; }

    /// <summary>PLN balance of a new cushion at the start of the effective month, default 0.</summary>
    public decimal? StartingBalance { get; init; }
}
