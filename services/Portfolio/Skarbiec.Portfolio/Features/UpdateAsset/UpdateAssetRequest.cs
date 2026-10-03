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

    public decimal? ManualValue { get; init; }

    public DateOnly? ManualValueDate { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasInstrument = InstrumentId is not null;
        var hasAnyManual = ManualValue is not null || ManualValueDate is not null;
        var hasFullManual = ManualValue is not null && ManualValueDate is not null;

        if (hasInstrument && hasAnyManual)
        {
            return
            [
                new ValidationResult(
                    "An asset is exactly one of market (InstrumentId), manual (ManualValue + ManualValueDate), or currency-valued (neither) — never more than one.",
                    [nameof(InstrumentId), nameof(ManualValue), nameof(ManualValueDate)])
            ];
        }

        if (hasInstrument)
        {
            return [];
        }

        if (hasAnyManual && !hasFullManual)
        {
            return
            [
                new ValidationResult(
                    "A manual asset requires both ManualValue and ManualValueDate.",
                    [nameof(ManualValue), nameof(ManualValueDate)])
            ];
        }

        if (hasFullManual || AssetValuationModes.SupportsCurrencyValued(AssetClass))
        {
            return [];
        }

        return
        [
            new ValidationResult(
                $"AssetClass '{AssetClass}' needs either InstrumentId (market) or ManualValue + ManualValueDate (manual) — only {string.Join(", ", AssetValuationModes.CurrencyValuedClasses)} may be created with neither (currency-valued).",
                [nameof(InstrumentId), nameof(ManualValue), nameof(ManualValueDate)])
        ];
    }
}
