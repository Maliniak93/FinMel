namespace Skarbiec.Contracts.Events;

/// <summary>Published when an FX history backfill stored rates for at least one currency.</summary>
public sealed record FxHistoryBackfilled
{
    /// <summary>First date of the rates the run added, inclusive.</summary>
    public required DateOnly From { get; init; }

    /// <summary>Last date of the rates the run added, inclusive.</summary>
    public required DateOnly To { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}
