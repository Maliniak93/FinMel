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

    public Guid? InstrumentId { get; init; }

    /// <summary>Optional opening transaction; omitted, the asset starts with no transactions.</summary>
    public RecordTransactionRequest? InitialTransaction { get; init; }

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
