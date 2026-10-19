namespace Skarbiec.Contracts.Events;

/// <summary>Carries the full position state, so a consumer never calls Portfolio back.</summary>
public sealed record AssetPositionChanged
{
    public required Guid AssetId { get; init; }
    public Guid? AccountId { get; init; }
    public required Guid UserId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }

    /// <summary>Market mode only.</summary>
    public Guid? InstrumentId { get; init; }

    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Quote units per unit of Quantity: grams of fine metal per piece for PreciousMetal, 1 for every other class.</summary>
    public required decimal QuoteUnitsPerQuantity { get; init; }

    /// <summary>Date of the asset's earliest transaction; null when it has none.</summary>
    public DateOnly? FirstTransactionDate { get; init; }

    /// <summary>End-of-day quantity on each distinct transaction date, ascending; empty when there are no transactions. The last point equals Quantity.</summary>
    public required IReadOnlyList<QuantityPoint> QuantityHistory { get; init; }

    /// <summary>Class in effect from each Date on, ascending; empty means AssetClass holds on every date. Only a Brokerage account's cash carries points.</summary>
    public required IReadOnlyList<ClassPoint> ClassHistory { get; init; }

    /// <summary>The asset's own archived flag.</summary>
    public required bool IsArchived { get; init; }

    /// <summary>Strictly increasing per AssetId from 0; xmin would not move when only the archive flag changes.</summary>
    public required long Version { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}
