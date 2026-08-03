namespace Skarbiec.MarketData.Data;

public enum SyncRunStatus
{
    Running,
    Completed,
    Partial,
    Failed,
}

// Global, not user-owned (same reasoning as Instrument/PriceQuote/FxRate) — one row per
// PriceSyncJob (T2.6) execution, the "has it run lately" source of truth for T2.14's status endpoint.
public sealed class SyncRun
{
    public required Guid Id { get; init; }
    public required DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public required SyncRunStatus Status { get; set; }
    public int SyncedCount { get; set; }
    public int NoDataCount { get; set; }
    public int FailedCount { get; set; }
}
