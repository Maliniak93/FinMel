using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Valuation;

/// <summary>
/// One valued position — exactly the shape of the <c>AssetValuation</c> line the
/// <c>DailyPricesSynced</c> consumer persists, minus the storage keys it fills in itself. There is
/// always one of these per input <see cref="ValuationPosition"/>: a position with no usable quote or
/// rate produces a zero-valued, <see cref="IsStale"/> line rather than being dropped (spec-03 AC10),
/// so "one line per position" holds without the consumer having to reconcile anything.
/// </summary>
public sealed record ValuedPosition
{
    public required Guid AssetId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Market mode only — the last known quote used; null otherwise, and null when no quote existed at all.</summary>
    public decimal? PriceUsed { get; init; }

    public DateOnly? PriceDate { get; init; }

    /// <summary>The currency→PLN rate applied (1 for a PLN position); null when none could be resolved.</summary>
    public decimal? FxRateUsed { get; init; }

    public required decimal ValuePln { get; init; }

    /// <summary>True when the quote or rate used is more than 7 days older than the snapshot date, or was missing entirely (03-domain-model.md §Valuation algorithm).</summary>
    public required bool IsStale { get; init; }
}
