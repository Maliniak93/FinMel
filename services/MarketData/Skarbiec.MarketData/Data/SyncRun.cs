namespace Skarbiec.MarketData.Data;

public enum SyncRunStatus
{
    Running,
    Completed,
    Partial,
    Failed,
}

// Global, not user-owned (same reasoning as Instrument/PriceQuote/FxRate) — one row per job
// execution, the "has it run lately" source of truth for T2.14's status endpoint. All three jobs
// (PriceSyncJob, FxSyncJob, HistoryBackfillJob) write here, told apart by Kind (spec-04).
public sealed class SyncRun
{
    public required Guid Id { get; init; }
    public required SyncRunKind Kind { get; init; }
    public required DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public required SyncRunStatus Status { get; set; }
    public int SyncedCount { get; set; }
    public int NoDataCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>Records a run's outcome. Shared by all three jobs so "Partial" means the same thing in
    /// each: something failed, but something else synced or legitimately came back empty.</summary>
    public void Finish(DateTimeOffset finishedAt, int synced, int noData, int failed)
    {
        FinishedAt = finishedAt;
        SyncedCount = synced;
        NoDataCount = noData;
        FailedCount = failed;
        Status = failed switch
        {
            0 => SyncRunStatus.Completed,
            _ when synced > 0 || noData > 0 => SyncRunStatus.Partial,
            _ => SyncRunStatus.Failed,
        };
    }
}
