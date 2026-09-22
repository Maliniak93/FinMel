using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Reporting.Data;

/// <summary>
/// Reporting's local read model of one Portfolio asset (03-domain-model.md §Reporting, ADR-021):
/// the full state carried by <c>AssetPositionChanged</c> plus <see cref="UpdatedAt"/>, upserted by
/// <c>AssetPositionChangedConsumer</c> and deleted by <c>AssetRemovedConsumer</c>. It exists so the
/// daily <c>DailyPricesSynced</c> valuation never calls Portfolio over REST — the event already
/// carried everything a valuation needs (spec-03).
/// </summary>
public sealed class Position : IUserOwned
{
    /// <summary>
    /// Primary key: Portfolio's own asset id, globally unique, so one row per asset holds by
    /// definition (spec-03 design decision 3). Cross-service reference, plain <see cref="Guid"/>,
    /// no FK (ADR-003) — a dangling id after a missed <c>AssetRemoved</c> is a normal state.
    /// </summary>
    public required Guid AssetId { get; init; }

    /// <summary>
    /// Set directly by the consumers from the event, not stamped by
    /// <see cref="UserOwnedSaveInterceptor"/> — see that type's remarks for why a message-driven
    /// writer with no request user needs the escape hatch.
    /// </summary>
    public Guid UserId { get; set; }

    public required Guid PortfolioId { get; set; }
    public required AssetClass AssetClass { get; set; }
    public required AssetValuationMode ValuationMode { get; set; }

    /// <summary>Market mode only — a MarketData instrument id, no FK (ADR-003).</summary>
    public Guid? InstrumentId { get; set; }

    public required string Currency { get; set; }
    public required decimal Quantity { get; set; }

    /// <summary>Manual mode only — set together with <see cref="ManualValueDate"/>, null otherwise.</summary>
    public decimal? ManualValueAmount { get; set; }

    public DateOnly? ManualValueDate { get; set; }

    /// <summary>
    /// The owning portfolio's archived flag, kept current by both the per-asset fan-out and the
    /// <c>PortfolioArchived</c>/<c>PortfolioRestored</c> consumers. Archived portfolios are skipped
    /// by the daily valuation.
    /// </summary>
    public required bool PortfolioIsArchived { get; set; }

    /// <summary>
    /// The publisher's per-asset ordering counter (<c>AssetPositionChanged.Version</c>). An event
    /// older than this is dropped, so an out-of-order redelivery can never resurrect an older
    /// quantity (spec-03 design decision 4).
    /// </summary>
    public required long Version { get; set; }

    /// <summary>When this read model row was last written — diagnostics only, never part of ordering (that is <see cref="Version"/>).</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
