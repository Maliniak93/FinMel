using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

// Reporting's read model of one Portfolio asset, so valuation never calls Portfolio.
public sealed class Position : IUserOwned
{
    // Portfolio's asset id: a dangling id after a missed AssetRemoved is a normal state.
    public required Guid AssetId { get; init; }

    // Set by the consumers from the event, not stamped by UserOwnedSaveInterceptor.
    public Guid UserId { get; set; }

    public Guid? AccountId { get; set; }
    public required AssetClass AssetClass { get; set; }
    public required AssetValuationMode ValuationMode { get; set; }

    public Guid? InstrumentId { get; set; }

    public required string Currency { get; set; }
    public required decimal Quantity { get; set; }

    // Market mode multiplier: the price is per quote unit (a gram of metal), the quantity in pieces.
    public decimal QuoteUnitsPerQuantity { get; set; } = 1m;

    // A position is valued unless this is set.
    public required bool IsArchived { get; set; }

    // End-of-day quantity per transaction date, ascending; empty for a position with no transactions.
    public List<PositionQuantityPoint> QuantityHistory { get; set; } = [];

    // UTC date of the event that archived the asset; null while it is not archived.
    public DateOnly? ArchivedOn { get; set; }

    // The publisher's per-asset counter: an older event is dropped, so a redelivery never resurrects an older quantity.
    public required long Version { get; set; }

    // Diagnostics only, never part of ordering.
    public required DateTimeOffset UpdatedAt { get; set; }
}
