using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.GetDashboard;

public sealed record DashboardResponse
{
    public required decimal NetWorthPln { get; init; }

    /// <summary>Date of the user's latest snapshot; null before any snapshot exists.</summary>
    public required DateOnly? AsOf { get; init; }

    /// <summary>The latest snapshot used a stale price or rate.</summary>
    public required bool IsStale { get; init; }

    public required IReadOnlyList<DashboardAssetClassValue> ByAssetClass { get; init; }
    public required IReadOnlyList<DashboardAccountValue> ByAccount { get; init; }
    public required IReadOnlyList<DashboardGroupValue> ByGroup { get; init; }
}

public sealed record DashboardGroupValue
{
    /// <summary>Null groups the lines of assets that sit in no group.</summary>
    public Guid? GroupId { get; init; }
    public required decimal ValuePln { get; init; }
    public required decimal Percentage { get; init; }
}

public sealed record DashboardAssetClassValue
{
    public required AssetClass AssetClass { get; init; }
    public required decimal ValuePln { get; init; }
    public required decimal Percentage { get; init; }
}

public sealed record DashboardAccountValue
{
    /// <summary>Null groups the lines of assets that sit in no account.</summary>
    public Guid? AccountId { get; init; }
    public required decimal ValuePln { get; init; }

    public required decimal Percentage { get; init; }
    public required DateOnly SnapshotDate { get; init; }
    public required bool IsStale { get; init; }
}
