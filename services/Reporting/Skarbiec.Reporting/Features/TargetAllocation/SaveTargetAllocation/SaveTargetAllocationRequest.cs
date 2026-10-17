using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;

public sealed record SaveTargetAllocationRequest
{
    /// <summary>The whole allocation, in display order; replaces what was saved.</summary>
    public required IReadOnlyList<SaveTargetAllocationGroup> Groups { get; init; }
}

public sealed record SaveTargetAllocationGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<AssetClass> AssetClasses { get; init; }

    /// <summary>Percent of the allocation base, 0-100 with one decimal.</summary>
    public required decimal TargetPercent { get; init; }

    /// <summary>Tolerance in percentage points either side of the target, 0-100 with one decimal.</summary>
    public required decimal BandPp { get; init; }
}
