using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Valuation;

/// <summary>One per input position; a position with no usable quote or rate gets a zero-valued, stale line.</summary>
public sealed record ValuedPosition
{
    public required Guid AssetId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Market mode only; null when no quote existed.</summary>
    public decimal? PriceUsed { get; init; }

    public DateOnly? PriceDate { get; init; }

    /// <summary>1 for a PLN position; null when no rate could be resolved.</summary>
    public decimal? FxRateUsed { get; init; }

    public required decimal ValuePln { get; init; }

    /// <summary>The quote or rate is more than 7 days older than the snapshot date, or missing.</summary>
    public required bool IsStale { get; init; }
}
