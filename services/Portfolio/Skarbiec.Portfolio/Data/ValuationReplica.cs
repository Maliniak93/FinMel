using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

// Reporting's latest valuation as last published; written only by LatestValuationChangedConsumer.
public sealed class ValuationReplica : IUserOwned
{
    public Guid UserId { get; set; }

    public required DateOnly Date { get; set; }
    public required bool IsStale { get; set; }
    public required decimal TotalPln { get; set; }
    public List<ValuationReplicaClassValue> ByAssetClass { get; set; } = [];
    public required DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed record ValuationReplicaClassValue(AssetClass AssetClass, decimal ValuePln);
