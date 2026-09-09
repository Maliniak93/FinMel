using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Valuation;

/// <summary>
/// One asset as the pure algorithm needs it — decoupled from Portfolio's own HTTP response shape
/// (<c>PositionForValuationResponse</c>) so this stays unit-testable without a service reference.
/// <see cref="ValuationMode"/> is the explicit source of truth for which branch
/// <see cref="ValuationAlgorithm.Calculate"/> takes (M1.4) — 03-domain-model.md §valuation algorithm.
/// </summary>
public sealed record ValuationPosition
{
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public Guid? InstrumentId { get; init; }

    /// <summary>Manual assets only — <see cref="ManualValueDate"/> isn't part of the formula (03-domain-model.md: FX is at snapshot date, not this date), it's a UI-only "refresh reminder", so it's deliberately not carried here.</summary>
    public decimal? ManualValueAmount { get; init; }
}
