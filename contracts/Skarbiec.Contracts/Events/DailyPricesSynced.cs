namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by MarketData's <c>PriceSyncJob</c> when a daily sync run finishes fully or partially
/// synced (T2.10). Lean payload by design — Reporting pulls positions/prices/FX via REST once
/// triggered, it doesn't need per-instrument detail here.
/// </summary>
public sealed record DailyPricesSynced
{
    public required Guid RunId { get; init; }
    public required DateOnly SyncDate { get; init; }
    public required int SyncedCount { get; init; }
    public required int FailedCount { get; init; }
    public required int NoDataCount { get; init; }
}
