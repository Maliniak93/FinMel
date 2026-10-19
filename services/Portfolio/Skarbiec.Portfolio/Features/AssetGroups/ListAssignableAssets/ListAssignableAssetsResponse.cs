using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.AssetGroups.ListAssignableAssets;

public sealed record AssignableAssetResponse(
    Guid AssetId,
    string Name,
    AssetClass AssetClass,
    string Currency,
    Guid? AccountId,
    string? AccountName,
    string? CashPlaceName,
    Guid? GroupId,
    bool IsArchived);
