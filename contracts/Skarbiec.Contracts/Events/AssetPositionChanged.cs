namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by Portfolio whenever an asset's position changes — created, edited, a transaction
/// recorded/edited/deleted against it, the asset itself archived/restored (asset-archive), or its
/// portfolio archived/restored (spec-02, ADR-021).
/// Carries the **full** position state, so a consumer never calls Portfolio back to learn what a
/// change means: everything a valuation needs (mode, instrument, currency, quantity, manual value,
/// whether the owning portfolio or the asset itself is archived) travels here.
/// </summary>
public sealed record AssetPositionChanged
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }

    /// <summary>Market mode only — a MarketData instrument id, no FK (ADR-003).</summary>
    public Guid? InstrumentId { get; init; }

    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Manual mode only — set together with <see cref="ManualValueDate"/>, null otherwise.</summary>
    public decimal? ManualValueAmount { get; init; }

    public DateOnly? ManualValueDate { get; init; }

    /// <summary>The owning portfolio's archived flag at publish time — archive/restore fans one event out per asset so a read model stops/resumes valuing it.</summary>
    public required bool PortfolioIsArchived { get; init; }

    /// <summary>
    /// The asset's own archived flag (asset-archive), independent of <see cref="PortfolioIsArchived"/>:
    /// an archived asset drops out of net worth from the day it is archived and rejoins on restore. The
    /// portfolio archive/restore fan-out carries it unchanged.
    /// </summary>
    public required bool IsArchived { get; init; }

    /// <summary>
    /// Strictly increasing per <see cref="AssetId"/>: a consumer applying events out of order keeps
    /// the highest version it has seen. An explicit counter rather than Postgres's <c>xmin</c>, which
    /// wraps and — decisively — does not move when only the portfolio's archived flag changes
    /// (spec-02 design decision 1). A newly created asset is published at version 0.
    /// </summary>
    public required long Version { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}
