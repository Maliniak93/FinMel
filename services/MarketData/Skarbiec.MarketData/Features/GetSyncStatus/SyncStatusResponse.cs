using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetSyncStatus;

/// <summary>The "has it run lately" status the T2.14 UI button reads (T2.6's <see cref="SyncRun"/>
/// is the source of truth). <see cref="HasRun"/> is false only when the job has never run at all —
/// e.g. a fresh environment before its first cron fire or manual trigger.</summary>
public sealed record SyncStatusResponse
{
    public required bool HasRun { get; init; }
    public Guid? RunId { get; init; }
    public SyncRunStatus? Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public int? SyncedCount { get; init; }
    public int? NoDataCount { get; init; }
    public int? FailedCount { get; init; }
}
