namespace Skarbiec.Contracts.Events;

public enum AssetChangeKind
{
    Created,
    Updated,
    Removed,
}

/// <summary>Published by Portfolio when an asset is added, updated or removed.</summary>
public sealed record AssetChanged
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
    public required AssetChangeKind Kind { get; init; }
}
