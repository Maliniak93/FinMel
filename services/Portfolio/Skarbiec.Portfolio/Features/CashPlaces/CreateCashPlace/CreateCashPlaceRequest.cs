using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.CashPlaces.CreateCashPlace;

public sealed record CreateCashPlaceRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>One or more distinct supported currencies, one balance each; PLN alone when omitted.</summary>
    public IReadOnlyList<string> Currencies { get; init; } = [SupportedCurrencies.Default];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Currencies.Count == 0)
        {
            yield return new ValidationResult("A cash place needs at least one currency.", [nameof(Currencies)]);
        }

        if (Currencies.Any(c => !SupportedCurrencies.Contains(c)))
        {
            yield return new ValidationResult(
                $"Accepted currencies: {SupportedCurrencies.Accepted}.", [nameof(Currencies)]);
        }

        if (Currencies.Distinct(StringComparer.Ordinal).Count() != Currencies.Count)
        {
            yield return new ValidationResult("Each currency can appear only once.", [nameof(Currencies)]);
        }
    }
}
