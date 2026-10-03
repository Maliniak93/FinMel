namespace Skarbiec.Contracts.Events;

/// <summary>Published when a price or FX sync run finishes fully or partially synced.</summary>
public sealed record DailyPricesSynced
{
    public required Guid RunId { get; init; }
    public required DateOnly SyncDate { get; init; }
    public required int SyncedCount { get; init; }
    public required int FailedCount { get; init; }
    public required int NoDataCount { get; init; }

    /// <summary>Not required, so a payload without it still reads, as Prices.</summary>
    public PriceSyncKind Kind { get; init; }
}
