namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by Portfolio when a portfolio is hard-deleted (spec-02). The delete cascades to its
/// assets and their transactions (spec-08), so it is accompanied in the same transaction by one
/// <see cref="AssetRemoved"/> per asset carrying <c>CascadedFromPortfolio = true</c> — a consumer
/// tracking per-asset state needs no portfolio-to-asset mapping of its own.
/// </summary>
public sealed record PortfolioDeleted
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
