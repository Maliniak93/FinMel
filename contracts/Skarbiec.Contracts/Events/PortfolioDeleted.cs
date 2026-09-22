namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by Portfolio when a portfolio is hard-deleted (spec-02). Only an empty portfolio can be
/// deleted — one holding assets is a 409 pointing at archive — so no <see cref="AssetRemoved"/>
/// accompanies it.
/// </summary>
public sealed record PortfolioDeleted
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
