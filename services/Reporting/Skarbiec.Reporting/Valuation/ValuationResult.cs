namespace Skarbiec.Reporting.Valuation;

public sealed record ValuationResult
{
    public required decimal TotalPln { get; init; }
    public required IReadOnlyList<AssetClassBreakdownEntry> Breakdown { get; init; }

    /// <summary>True when any position priced into this result used a quote/rate more than 7 days before the snapshot date, or had no quote/rate at all (03-domain-model.md §Valuation algorithm).</summary>
    public required bool IsStale { get; init; }
}
