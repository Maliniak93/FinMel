using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetSyncStatus;

/// <summary>HasRun is false only when no job of any kind has ever run; a kind that never ran is null.</summary>
public sealed record SyncStatusResponse
{
    public required bool HasRun { get; init; }
    public SyncRunSummary? Prices { get; init; }
    public SyncRunSummary? Fx { get; init; }
    public SyncRunSummary? Backfill { get; init; }
    public SyncRunSummary? BondCatalog { get; init; }
}

/// <summary>The latest run of one kind, including one still in progress.</summary>
public sealed record SyncRunSummary
{
    public required Guid RunId { get; init; }
    public required SyncRunStatus Status { get; init; }

    /// <summary>What started the run: its cron schedule, the service start or the manual button.</summary>
    public required SyncRunSource Source { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public required int SyncedCount { get; init; }
    public required int NoDataCount { get; init; }
    public required int FailedCount { get; init; }
}
