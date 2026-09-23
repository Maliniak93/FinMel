namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by Portfolio when an asset is deleted — on its own (spec-02) or as part of deleting its
/// portfolio, which fans one out per asset alongside <see cref="PortfolioDeleted"/> (spec-08). Its
/// transactions are deleted with it. Terminal for this <see cref="AssetId"/> — Portfolio never
/// mutates an asset after deleting it and a re-created asset gets a fresh <see cref="Guid"/> — so it
/// deliberately carries no version: the inbox's <c>MessageId</c> dedup already covers redelivery
/// (spec-02 design decision 3).
/// </summary>
public sealed record AssetRemoved
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>
    /// True when this removal is part of a portfolio delete (spec-08): a
    /// <see cref="PortfolioDeleted"/> for <see cref="PortfolioId"/> is published in the same
    /// transaction, so a consumer holding per-portfolio aggregates should leave them to that event
    /// instead of recomputing them for a portfolio that is going away.
    /// </summary>
    public required bool CascadedFromPortfolio { get; init; }
}
