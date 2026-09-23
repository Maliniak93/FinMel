using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetSyncStatus;

/// <summary>The "has it run lately" status the T2.14 UI reads, one summary per job kind (spec-04
/// design decision 14; <see cref="SyncRun"/> is the source of truth). <see cref="HasRun"/> is false
/// only when no job of any kind has ever run — e.g. a fresh environment before its first cron fire or
/// manual trigger. A kind that has never run is null.</summary>
public sealed record SyncStatusResponse
{
    public required bool HasRun { get; init; }
    public SyncRunSummary? Prices { get; init; }
    public SyncRunSummary? Fx { get; init; }
    public SyncRunSummary? Backfill { get; init; }
}

/// <summary>The latest <see cref="SyncRun"/> of one kind — including one still in progress.</summary>
public sealed record SyncRunSummary
{
    public required Guid RunId { get; init; }
    public required SyncRunStatus Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public required int SyncedCount { get; init; }
    public required int NoDataCount { get; init; }
    public required int FailedCount { get; init; }
}
