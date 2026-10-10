using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public sealed record UpdateAssetRequest : IValidatableObject
{
    public required AssetClass AssetClass { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required, SupportedCurrency]
    public required string Currency { get; init; }

    public Guid? InstrumentId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InstrumentId is not null || AssetValuationModes.SupportsCurrencyValued(AssetClass))
        {
            return [];
        }

        return
        [
            new ValidationResult(
                $"AssetClass '{AssetClass}' needs an InstrumentId (market) — only {string.Join(", ", AssetValuationModes.CurrencyValuedClasses)} may be created without one (currency-valued).",
                [nameof(InstrumentId)])
        ];
    }
}
