namespace Skarbiec.Contracts.Events;

/// <summary>Accompanied by one AssetRemoved per asset with CascadedFromPortfolio set.</summary>
public sealed record PortfolioDeleted
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
