namespace Skarbiec.Contracts.Events;

public sealed record WatchlistItemAdded
{
    public required Guid WatchlistItemId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid InstrumentId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
