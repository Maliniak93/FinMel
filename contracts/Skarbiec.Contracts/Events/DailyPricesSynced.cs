namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by MarketData's <c>PriceSyncJob</c> (<see cref="PriceSyncKind.Prices"/>) and
/// <c>FxSyncJob</c> (<see cref="PriceSyncKind.Fx"/>) when a run finishes fully or partially synced
/// (T2.10, spec-04). Lean payload by design — Reporting pulls positions/prices/FX via REST once
/// triggered, it doesn't need per-instrument detail here.
/// </summary>
public sealed record DailyPricesSynced
{
    public required Guid RunId { get; init; }
    public required DateOnly SyncDate { get; init; }
    public required int SyncedCount { get; init; }
    public required int FailedCount { get; init; }
    public required int NoDataCount { get; init; }

    /// <summary>Not <c>required</c>: System.Text.Json enforces <c>required</c> on deserialization, and a
    /// payload without the field must still read (as <see cref="PriceSyncKind.Prices"/>, the zero value).</summary>
    public PriceSyncKind Kind { get; init; }
}
