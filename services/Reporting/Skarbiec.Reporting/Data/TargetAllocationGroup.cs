using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Data;

/// <summary>Target share of the allocation base in percent, with a tolerance of plus or minus BandPp percentage points.</summary>
public sealed record TargetAllocationGroup(string Name, IReadOnlyList<AssetClass> AssetClasses, decimal TargetPercent, decimal BandPp);
