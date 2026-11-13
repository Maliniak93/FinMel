namespace Skarbiec.Portfolio.Features.AssetGroups.SetAssetGroupMembers;

public sealed record SetAssetGroupMembersRequest
{
    /// <summary>The accounts checked for the group; an account of the group that is not listed leaves it.</summary>
    public required IReadOnlyList<Guid> AccountIds { get; init; }

    /// <summary>The Stock, Etf, Bond and stand-alone assets checked for the group.</summary>
    public required IReadOnlyList<Guid> AssetIds { get; init; }
}
