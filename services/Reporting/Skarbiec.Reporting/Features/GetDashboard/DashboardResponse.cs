using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.GetDashboard;

public sealed record DashboardResponse
{
    public required decimal NetWorthPln { get; init; }

    /// <summary>Latest snapshot date across the included portfolios; null before any snapshot exists.</summary>
    public required DateOnly? AsOf { get; init; }

    /// <summary>Any included portfolio's latest snapshot is stale; portfolios can lag independently.</summary>
    public required bool IsStale { get; init; }

    public required IReadOnlyList<DashboardAssetClassValue> ByAssetClass { get; init; }
    public required IReadOnlyList<DashboardPortfolioValue> ByPortfolio { get; init; }
}

public sealed record DashboardAssetClassValue
{
    public required AssetClass AssetClass { get; init; }
    public required decimal ValuePln { get; init; }
    public required decimal Percentage { get; init; }
}

public sealed record DashboardPortfolioValue
{
    public required Guid PortfolioId { get; init; }
    public required decimal ValuePln { get; init; }
    public required decimal Percentage { get; init; }
    public required DateOnly SnapshotDate { get; init; }
    public required bool IsStale { get; init; }
}
