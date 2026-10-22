using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups;

public sealed record AssetGroupResponse(
    Guid Id,
    string Name,
    string Color,
    decimal? TargetPln,
    int AssetCount,
    AssetGroupKind Kind,
    decimal MonthlyContributionPln,
    List<AssetGroupClassShare> ClassAllocation,
    decimal? SharePercent);
