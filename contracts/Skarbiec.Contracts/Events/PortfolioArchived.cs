namespace Skarbiec.Contracts.Events;

/// <summary>Accompanied by one AssetPositionChanged per asset with PortfolioIsArchived set; a repeat archive publishes nothing.</summary>
public sealed record PortfolioArchived
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
