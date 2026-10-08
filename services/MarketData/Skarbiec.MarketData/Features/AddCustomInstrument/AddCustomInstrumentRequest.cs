using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

/// <summary>The price source is derived from AssetClass, never asked for.</summary>
public sealed record AddCustomInstrumentRequest : IValidatableObject
{
    [Required, MaxLength(30)]
    public required string Ticker { get; init; }

    [Required, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>Required for Crypto; ignored for Stock and Etf, whose currency comes from the exchange the ticker suffix names.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? QuoteCurrency { get; init; }

    public required AssetClass AssetClass { get; init; }

    /// <summary>When the provider is unreachable, create the instrument Unverified instead of failing with 503.</summary>
    public bool AllowUnverified { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AssetClass is not (AssetClass.Stock or AssetClass.Etf) && string.IsNullOrWhiteSpace(QuoteCurrency))
        {
            yield return new ValidationResult(
                $"QuoteCurrency is required for asset class '{AssetClass}'.", [nameof(QuoteCurrency)]);
        }
    }
}
