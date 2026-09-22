namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by Portfolio when an archived portfolio is restored (spec-02) — the mirror of
/// <see cref="PortfolioArchived"/>, with one <see cref="AssetPositionChanged"/> per asset carrying
/// <c>PortfolioIsArchived = false</c> in the same transaction. Restoring a portfolio that is not
/// archived publishes nothing.
/// </summary>
public sealed record PortfolioRestored
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
