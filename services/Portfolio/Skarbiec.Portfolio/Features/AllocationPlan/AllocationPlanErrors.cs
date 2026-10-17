using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Planning.Allocation;

namespace Skarbiec.Portfolio.Features.AllocationPlan;

internal static class AllocationPlanErrors
{
    public static Error BucketNotFound(Guid id) =>
        new("NotFound.AllocationBucket", $"Allocation bucket '{id}' was not found.");

    public static Error VersionNotFound(string month) =>
        new("NotFound.AllocationPlanVersion", $"No allocation plan starts in {month}.");

    public static Error BucketKindMismatch(Guid id) =>
        new("Validation.BucketKindMismatch", $"Allocation bucket '{id}' has another kind.");

    public static Error BucketAccountWrapper(Guid accountId, BucketKind kind) =>
        new("Validation.BucketAccountWrapper", $"Account '{accountId}' cannot back a {kind} bucket.");

    public static Error BalanceAdjustmentNotFound(Guid bucketId, string month) =>
        new("NotFound.BalanceAdjustment", $"Bucket '{bucketId}' has no balance adjustment in {month}.");

    public static Error BucketHasNoBalance(Guid id) =>
        new("Validation.BucketHasNoBalance", $"Allocation bucket '{id}' has no balance to adjust.");

    public static Error PercentSumOver100 { get; } =
        new("Validation.PercentSumOver100", "The percent shares add up to more than 100.");
}
