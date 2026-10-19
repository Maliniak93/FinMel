using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.AssetGroups.UpdateAssetGroup;

public sealed record UpdateAssetGroupRequest
{
    [Required]
    public required string Name { get; init; }

    [Required]
    public required string Color { get; init; }

    /// <summary>PLN target value of the group; null when it has none.</summary>
    public decimal? TargetPln { get; init; }
}
