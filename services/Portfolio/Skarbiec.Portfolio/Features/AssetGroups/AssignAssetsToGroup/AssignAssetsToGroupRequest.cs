namespace Skarbiec.Portfolio.Features.AssetGroups.AssignAssetsToGroup;

public sealed record AssignAssetsToGroupRequest
{
    public required IReadOnlyList<Guid> AssetIds { get; init; }

    /// <summary>The target group; null removes the assets from their group.</summary>
    public Guid? GroupId { get; init; }
}
