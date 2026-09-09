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
    /// <remarks>
    /// M1.5: there is deliberately no <c>Quantity</c> property here anymore. ADR-009 makes
    /// transactions the only source of truth for an asset's quantity, and <c>AddAssetRequest</c>
    /// already dropped its own directly-settable <c>Quantity</c> in favour of an optional initial
    /// transaction (recomputed via <see cref="Skarbiec.Portfolio.Features.TransactionQuantityCalculator"/>).
    /// Keeping a writable <c>Quantity</c> here — even for currency-valued Cash/Deposit assets, whose
    /// value is <c>Quantity × FxRate</c> — would have reopened a second, parallel way to set the same
    /// number that <c>UpdateAssetHandler</c> could apply without going through the calculator at all.
    /// A currency-valued asset with no transactions values at 0, exactly like a market/manual asset
    /// with no transactions; its quantity only moves via <c>RecordTransaction</c>/<c>UpdateTransaction</c>/
    /// <c>DeleteTransaction</c> (typically <see cref="TransactionType.Deposit"/>/<see cref="TransactionType.Withdraw"/>
    /// for cash-like classes) or <c>AddAsset</c>'s own optional initial transaction — never a direct
    /// field on this request.
    /// </remarks>
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
