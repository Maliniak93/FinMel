using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Features.GetDashboard;

public sealed record DashboardResponse
{
    public required decimal NetWorthPln { get; init; }

    /// <summary>Latest snapshot date across every included portfolio; <c>null</c> when there's nothing to show yet (no snapshot has ever been computed for this user).</summary>
    public required DateOnly? AsOf { get; init; }

    /// <summary>True when any included portfolio's latest snapshot is itself stale (03-domain-model.md §Valuation algorithm) — not "the whole dashboard is old", since portfolios can lag independently after a per-portfolio compute failure (T2.11).</summary>
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
    public required DateOnly SnapshotDate { get; init; }
    public required bool IsStale { get; init; }
}
