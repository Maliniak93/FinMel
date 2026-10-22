using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class AssetGroup : IUserOwned
{
    public required Guid Id { get; init; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; }
    public decimal? TargetPln { get; set; }
    public AssetGroupKind Kind { get; set; }
    public decimal MonthlyContributionPln { get; set; }

    // Share of the free funds the Regular groups split between them (one decimal); null for a Cushion.
    public decimal? SharePercent { get; set; }
    public List<AssetGroupClassShare> ClassAllocation { get; set; } = [];
}

public enum AssetGroupKind
{
    Regular = 0,
    Cushion = 1
}

public sealed record AssetGroupClassShare(AssetClass AssetClass, decimal Percent);
