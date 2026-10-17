namespace Skarbiec.Contracts.Events;

public sealed record TargetAllocationSaved
{
    public required Guid UserId { get; init; }

    /// <summary>The whole target in the order the user saved it.</summary>
    public required IReadOnlyList<TargetAllocationGroupState> Groups { get; init; }

    public required DateTimeOffset SavedAtUtc { get; init; }
}

/// <summary>Target share in percent, with a tolerance of plus or minus BandPp percentage points.</summary>
public sealed record TargetAllocationGroupState
{
    public required string Name { get; init; }
    public required IReadOnlyList<AssetClass> AssetClasses { get; init; }
    public required decimal TargetPercent { get; init; }
    public required decimal BandPp { get; init; }
}
