namespace Skarbiec.Contracts.Events;

/// <summary>Terminal for the AssetId, so it carries no version; the inbox covers redelivery.</summary>
public sealed record AssetRemoved
{
    public required Guid AssetId { get; init; }
    public Guid? AccountId { get; init; }
    public required Guid UserId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
