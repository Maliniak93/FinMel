using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.AssetGroups.CreateAssetGroup;

public sealed record CreateAssetGroupRequest
{
    [Required]
    public required string Name { get; init; }

    [Required]
    public required string Color { get; init; }

    /// <summary>PLN target value of the group; null when it has none, required for a cushion.</summary>
    public decimal? TargetPln { get; init; }

    [EnumDataType(typeof(AssetGroupKind))]
    public AssetGroupKind Kind { get; init; }

    /// <summary>PLN set aside for the group each month.</summary>
    public decimal MonthlyContributionPln { get; init; }

    /// <summary>Target share of each asset class in the group, summing to 100; empty when the group has none.</summary>
    public List<AssetGroupClassShare>? ClassAllocation { get; init; }
}
