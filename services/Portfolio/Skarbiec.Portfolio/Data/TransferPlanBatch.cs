using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class TransferPlanBatch : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid PlanId { get; init; }

    /// <summary>1 for the plan itself.</summary>
    public required int Number { get; init; }

    public required DateTimeOffset AcceptedAtUtc { get; init; }
    public required DateOnly? ValuationDate { get; init; }
    public required int MinimumTransferPln { get; init; }

    public List<TransferPlanRow> Rows { get; init; } = [];
}
