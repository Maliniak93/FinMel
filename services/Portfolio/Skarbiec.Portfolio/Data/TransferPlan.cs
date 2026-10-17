using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class TransferPlan : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }

    /// <summary>The first day of the month.</summary>
    public required DateOnly Month { get; init; }

    public required bool IsSkipped { get; init; }

    public List<TransferPlanBatch> Batches { get; init; } = [];
}
