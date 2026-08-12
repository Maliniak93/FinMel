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

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    /// <summary>Market mode (T2.9): points at a MarketData instrument instead of a manual value. Switching modes is allowed; existing transactions are kept either way.</summary>
    public Guid? InstrumentId { get; init; }

    public decimal? ManualValue { get; init; }

    public DateOnly? ManualValueDate { get; init; }

    /// <summary>
    /// Exactly one of three modes (M1.4) — market (<see cref="InstrumentId"/>), manual
    /// (<see cref="ManualValue"/> + <see cref="ManualValueDate"/>), or currency-valued (neither) — see
    /// the same rule on <c>Asset.ValuationMode</c>. The currency-valued combination is only accepted
    /// for classes <c>AssetValuationModes.SupportsCurrencyValued</c> (Cash, Deposit): for every other
    /// class "neither" was always a validation error before M1.4 and stays one, so no pre-existing
    /// behaviour changes. Market and Manual stay available to every class exactly as before.
    /// </summary>
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
