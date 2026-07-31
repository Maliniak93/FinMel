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
    public decimal ManualValueAmount { get; set; }
    public required DateOnly ManualValueDate { get; set; }

    /// <summary>Guid from the MarketData database, no FK (ADR-003). Unused until Phase 2.</summary>
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
