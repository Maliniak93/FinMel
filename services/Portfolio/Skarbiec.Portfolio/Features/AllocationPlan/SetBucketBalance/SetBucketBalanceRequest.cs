using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.AllocationPlan.SetBucketBalance;

public sealed record SetBucketBalanceRequest
{
    /// <summary>PLN: the cushion's balance at the start of the month, or contributed in the month's year before it.</summary>
    [Range(0, 10_000_000)]
    public required decimal Amount { get; init; }
}
