namespace Skarbiec.Contracts.Events;

/// <summary>
/// Published by Portfolio when an asset is deleted (spec-02). Terminal for this
/// <see cref="AssetId"/> — Portfolio never mutates an asset after deleting it and a re-created asset
/// gets a fresh <see cref="Guid"/> — so it deliberately carries no version: the inbox's
/// <c>MessageId</c> dedup already covers redelivery (spec-02 design decision 3).
/// </summary>
public sealed record AssetRemoved
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
