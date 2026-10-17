using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.TargetAllocation.GetTargetAllocation;

public sealed record TargetAllocationResponse
{
    public required bool IsConfigured { get; init; }

    /// <summary>Date of the latest snapshot the figures come from; null before any snapshot exists.</summary>
    public required DateOnly? AsOf { get; init; }

    /// <summary>The latest snapshot used a stale price or rate.</summary>
    public required bool IsStale { get; init; }

    public required decimal TotalPln { get; init; }

    /// <summary>Sum of the values of the asset classes that sit in a group.</summary>
    public required decimal BasePln { get; init; }

    /// <summary>Sum of the values of the asset classes that sit in no group.</summary>
    public required decimal OutsidePln { get; init; }

    public required IReadOnlyList<AssetClass> OutsideAssetClasses { get; init; }
    public required IReadOnlyList<TargetAllocationAssetClassValue> ByAssetClass { get; init; }
    public required IReadOnlyList<TargetAllocationGroupResponse> Groups { get; init; }
}

public sealed record TargetAllocationAssetClassValue
{
    public required AssetClass AssetClass { get; init; }
    public required decimal ValuePln { get; init; }
}

public sealed record TargetAllocationGroupResponse
{
    public required string Name { get; init; }
    public required IReadOnlyList<AssetClass> AssetClasses { get; init; }

    /// <summary>Percent of the base, one decimal.</summary>
    public required decimal TargetPercent { get; init; }

    /// <summary>Tolerance in percentage points either side of the target.</summary>
    public required decimal BandPp { get; init; }

    public required decimal BandLowPercent { get; init; }
    public required decimal BandHighPercent { get; init; }
    public required decimal ValuePln { get; init; }

    /// <summary>Null when the base is not positive.</summary>
    public required decimal? CurrentPercent { get; init; }

    /// <summary>Current minus target in percentage points; null when the base is not positive.</summary>
    public required decimal? DeviationPp { get; init; }

    /// <summary>Target share of the base in PLN; the groups add up to the base exactly.</summary>
    public required decimal? TargetValuePln { get; init; }

    /// <summary>Value minus target value; negative means the group is short.</summary>
    public required decimal? DeviationPln { get; init; }

    public required bool? IsInBand { get; init; }
}
