using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed record AddAssetRequest : IValidatableObject
{
    public required AssetClass AssetClass { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = Money.BaseCurrency;

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    /// <summary>Market mode (T2.9): points at a MarketData instrument instead of a manual value.</summary>
    public Guid? InstrumentId { get; init; }

    public decimal? ManualValue { get; init; }

    public DateOnly? ManualValueDate { get; init; }

    /// <summary>Manual and market are mutually exclusive — see the same rule on <c>Asset</c>.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InstrumentId is not null && (ManualValue is not null || ManualValueDate is not null))
        {
            yield return new ValidationResult(
                "An asset is either market (InstrumentId) or manual (ManualValue + ManualValueDate), not both.",
                [nameof(InstrumentId), nameof(ManualValue), nameof(ManualValueDate)]);
        }

        if (InstrumentId is null && (ManualValue is null || ManualValueDate is null))
        {
            yield return new ValidationResult(
                "A manual asset requires both ManualValue and ManualValueDate.",
                [nameof(ManualValue), nameof(ManualValueDate)]);
        }
    }
}
