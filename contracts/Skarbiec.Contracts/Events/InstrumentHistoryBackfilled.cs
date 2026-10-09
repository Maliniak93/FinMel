namespace Skarbiec.Contracts.Events;

/// <summary>Published when a history backfill stored quotes for an instrument.</summary>
public sealed record InstrumentHistoryBackfilled
{
    public required Guid InstrumentId { get; init; }

    /// <summary>The window the backfill fetched, after clamping to the source's limit.</summary>
    public required DateOnly From { get; init; }

    /// <summary>The window the backfill fetched, after clamping to the source's limit.</summary>
    public required DateOnly To { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}
