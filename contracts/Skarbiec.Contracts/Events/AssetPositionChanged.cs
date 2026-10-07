namespace Skarbiec.Contracts.Events;

/// <summary>Carries the full position state, so a consumer never calls Portfolio back.</summary>
public sealed record AssetPositionChanged
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }

    /// <summary>Market mode only.</summary>
    public Guid? InstrumentId { get; init; }

    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Quote units per unit of Quantity: grams of fine metal per piece for PreciousMetal, 1 for every other class.</summary>
    public required decimal QuoteUnitsPerQuantity { get; init; }

    /// <summary>Manual mode only, set together with ManualValueDate.</summary>
    public decimal? ManualValueAmount { get; init; }

    public DateOnly? ManualValueDate { get; init; }

    /// <summary>The owning portfolio's archived flag at publish time.</summary>
    public required bool PortfolioIsArchived { get; init; }

    /// <summary>The asset's own archived flag, independent of PortfolioIsArchived.</summary>
    public required bool IsArchived { get; init; }

    /// <summary>Strictly increasing per AssetId from 0; xmin would not move when only the archive flag changes.</summary>
    public required long Version { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}
