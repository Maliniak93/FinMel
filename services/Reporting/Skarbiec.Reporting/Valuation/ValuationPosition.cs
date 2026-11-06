using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Reporting.Valuation;

public sealed record ValuationPosition
{
    public required Guid AssetId { get; init; }

    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public Guid? InstrumentId { get; init; }

    /// <summary>Market mode only: quote units per unit of Quantity, e.g. grams of fine metal per piece.</summary>
    public decimal QuoteUnitsPerQuantity { get; init; } = 1m;

    /// <summary>Treasury bond terms: the line is worth Quantity plus the interest no settlement has credited yet.</summary>
    public PositionBondTerms? BondTerms { get; init; }
}
