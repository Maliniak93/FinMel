using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.GetWealthSummary;

public sealed record WealthSummaryResponse
{
    public required decimal TotalNetWorth { get; init; }
    public required string BaseCurrency { get; init; }

    // Phase 1 has no FX rates (ADR-007: external price APIs only run from MarketData's Quartz
    // jobs, which don't exist until Phase 2) — non-PLN manual values are summed into
    // TotalNetWorth/Value as if they were already PLN. This flag lets the UI show a caveat instead
    // of a silently wrong number.
    // Phase 2: replaced by Reporting (T2.12/T2.13), which has real FX conversion.
    public required bool FxConversionIsNaive { get; init; }

    public required IReadOnlyList<AssetClassBreakdown> ByAssetClass { get; init; }
    public required IReadOnlyList<PortfolioBreakdown> ByPortfolio { get; init; }
}

public sealed record AssetClassBreakdown
{
    public required AssetClass AssetClass { get; init; }
    public required decimal Value { get; init; }
    public required decimal Percentage { get; init; }
}

public sealed record PortfolioBreakdown
{
    public required Guid PortfolioId { get; init; }
    public required string Name { get; init; }
    public required decimal Value { get; init; }
}
