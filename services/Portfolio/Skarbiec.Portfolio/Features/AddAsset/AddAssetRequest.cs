using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.RecordTransaction;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed record AddAssetRequest : IValidatableObject
{
    public required AssetClass AssetClass { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [SupportedCurrency]
    public string Currency { get; init; } = Money.BaseCurrency;

    /// <summary>Market mode (T2.9): points at a MarketData instrument instead of a manual value.</summary>
    public Guid? InstrumentId { get; init; }

    public decimal? ManualValue { get; init; }

    public DateOnly? ManualValueDate { get; init; }

    /// <summary>
    /// Optional opening transaction (M1.5, S5's "add first transaction" checkbox). Omitted → the
    /// asset is created with zero transactions and <c>Quantity == 0</c>, exactly as an asset with a
    /// checkbox left unchecked. Reuses <see cref="RecordTransactionRequest"/> — the shape of "one
    /// transaction" input — rather than inventing a second one; it is validated the same way (its own
    /// <c>[Range]</c>/<c>[Required]</c> attributes, recursed into automatically by .NET 10's Minimal
    /// API validation for nested complex properties) and turned into quantity by the same
    /// <see cref="TransactionQuantityCalculator"/> path <c>RecordTransaction</c> uses (ADR-009) — never
    /// a second, parallel way to arrive at a quantity.
    /// </summary>
    public RecordTransactionRequest? InitialTransaction { get; init; }

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
