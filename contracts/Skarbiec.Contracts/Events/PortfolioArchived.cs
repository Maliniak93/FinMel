namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by Portfolio when a portfolio is archived (spec-02). Accompanied in the same
/// transaction by one <see cref="AssetPositionChanged"/> per asset carrying
/// <c>PortfolioIsArchived = true</c>, so a consumer holding per-asset state needs no join back.
/// Archiving an already-archived portfolio publishes nothing — an event is a fact that happened.
/// </summary>
public sealed record PortfolioArchived
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
