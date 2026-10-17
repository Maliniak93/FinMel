using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

// Reporting's target allocation as last published; written only by TargetAllocationSavedConsumer.
public sealed class TargetAllocationReplica : IUserOwned
{
    public Guid UserId { get; set; }

    // In the order the user saved them.
    public List<TargetAllocationReplicaGroup> Groups { get; set; } = [];

    public required DateTimeOffset SavedAtUtc { get; set; }
}

/// <summary>Target share of the allocation base in percent, with a tolerance of plus or minus BandPp percentage points.</summary>
public sealed record TargetAllocationReplicaGroup(string Name, IReadOnlyList<AssetClass> AssetClasses, decimal TargetPercent, decimal BandPp);
