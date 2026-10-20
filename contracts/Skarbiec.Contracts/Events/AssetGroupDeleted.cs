namespace Skarbiec.Contracts.Events;

public sealed record AssetGroupDeleted
{
    public required Guid UserId { get; init; }

    public required Guid GroupId { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}
