using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public sealed record UpdateAssetRequest
{
    public required AssetClass AssetClass { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string Currency { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    public required decimal ManualValue { get; init; }

    public required DateOnly ManualValueDate { get; init; }
}
