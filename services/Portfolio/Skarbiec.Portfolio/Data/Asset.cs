using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class Asset : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required Guid PortfolioId { get; init; }
    public required AssetClass AssetClass { get; set; }
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>
    /// Which of the three valuation modes (M1.4, <c>Skarbiec.Contracts.AssetValuationMode</c>) this
    /// asset uses — the explicit source of truth for which of the fields below are populated:
    /// <see cref="InstrumentId"/> (Market), <see cref="ManualValueAmount"/> +
    /// <see cref="ManualValueDate"/> (Manual), or neither (CurrencyValued — value comes from
    /// <see cref="Quantity"/> × the FX rate for <see cref="Currency"/>, no extra field needed).
    /// Exactly one shape applies at a time. Enforced by <c>AddAssetRequest</c>/
    /// <c>UpdateAssetRequest</c>'s <c>IValidatableObject</c> rule, not a DB constraint — Postgres has
    /// no cheap "exactly one of these column groups is null" check across a nullable decimal +
    /// nullable Guid without a trigger, and the handler is already the single writer of all three.
    /// </summary>
    public required AssetValuationMode ValuationMode { get; set; }

    /// <summary>Manual mode only — set together with <see cref="ManualValueDate"/>, null otherwise.</summary>
    public decimal? ManualValueAmount { get; set; }

    public DateOnly? ManualValueDate { get; set; }

    /// <summary>Market mode only. Guid from the MarketData database, no FK (ADR-003). Validated via internal REST on write (T2.9).</summary>
    public Guid? InstrumentId { get; set; }

    /// <summary>
    /// Denormalized count of transactions against this asset. Transaction doesn't exist yet
    /// (T1.3, which depends on this task) — this counter lets RemoveAsset block a hard delete on
    /// an asset that still has transactions today (ADR-009: transactions are the source of truth
    /// for quantity, so removing an asset must not orphan its transaction history); T1.3's
    /// RecordTransaction/DeleteTransaction must increment/decrement it. Mirrors the AssetCount
    /// decision made on Portfolio in T1.1.
    /// </summary>
    public int TransactionCount { get; set; }
}
