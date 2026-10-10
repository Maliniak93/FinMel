namespace Skarbiec.Contracts.Events;

/// <summary>Carries the full position state, so a consumer never calls Portfolio back.</summary>
public sealed record AssetPositionChanged
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public required IReadOnlyList<QuantityPoint> QuantityHistory { get; init; }
}
