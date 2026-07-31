using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed record AddAssetRequest
{
    public required AssetClass AssetClass { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = Money.BaseCurrency;

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    public required decimal ManualValue { get; init; }

    public required DateOnly ManualValueDate { get; init; }
}
