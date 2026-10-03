namespace Skarbiec.Contracts.Events;

/// <summary>Accompanied by one AssetPositionChanged per asset with PortfolioIsArchived cleared; restoring an active portfolio publishes nothing.</summary>
public sealed record PortfolioRestored
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
