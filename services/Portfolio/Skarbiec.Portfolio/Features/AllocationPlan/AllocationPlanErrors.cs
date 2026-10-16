using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.AllocationPlan;

internal static class AllocationPlanErrors
{
    public static Error BucketNotFound(Guid id) =>
        new("NotFound.AllocationBucket", $"Allocation bucket '{id}' was not found.");

    public static Error VersionNotFound(string month) =>
        new("NotFound.AllocationPlanVersion", $"No allocation plan starts in {month}.");

    public static Error BucketKindMismatch(Guid id) =>
        new("Validation.BucketKindMismatch", $"Allocation bucket '{id}' has another kind.");

    public static Error PercentSumOver100 { get; } =
        new("Validation.PercentSumOver100", "The percent shares add up to more than 100.");
}
