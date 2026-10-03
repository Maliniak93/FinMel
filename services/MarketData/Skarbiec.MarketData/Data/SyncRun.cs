namespace Skarbiec.MarketData.Data;

public enum SyncRunStatus
{
    Running,
    Completed,
    Partial,
    Failed,
}

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

    // Shared by all three jobs, so Partial means the same in each: something failed, but something else synced or came back empty.
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
