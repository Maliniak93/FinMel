using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Valuation;

/// <summary>
/// One asset as the pure algorithm needs it — decoupled from the <c>Position</c> read model's own
/// storage shape so this stays unit-testable with no DbContext.
/// <see cref="ValuationMode"/> is the explicit source of truth for which branch
/// <see cref="ValuationAlgorithm.Calculate"/> takes (M1.4) — 03-domain-model.md §valuation algorithm.
/// </summary>
public sealed record ValuationPosition
{
    /// <summary>Carried through onto the produced <see cref="ValuedPosition"/> so the consumer can key each line by asset without re-zipping inputs to outputs (spec-03).</summary>
    public required Guid AssetId { get; init; }

    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public Guid? InstrumentId { get; init; }

    /// <summary>Manual assets only — <see cref="ManualValueDate"/> isn't part of the formula (03-domain-model.md: FX is at snapshot date, not this date), it's a UI-only "refresh reminder", so it's deliberately not carried here.</summary>
    public decimal? ManualValueAmount { get; init; }
}
