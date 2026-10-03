using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Contracts;

// User-chosen currencies only: MarketData keeps whatever its providers quote, and codes match exactly in ISO 4217 uppercase.
public static class SupportedCurrencies
{
    public const string Default = Money.BaseCurrency;

    // In the order the UI should offer them.
    public static readonly IReadOnlyList<string> All = [Money.BaseCurrency, "EUR", "USD"];

    public static string Accepted { get; } = string.Join(", ", All);

    public static bool Contains(string? currency) =>
        currency is not null && All.Contains(currency, StringComparer.Ordinal);

    public static Result Validate(string? currency) =>
        Contains(currency) ? Result.Success() : CurrencyErrors.Unsupported(currency);
}

// null passes: [Required] owns that case.
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SupportedCurrencyAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || SupportedCurrencies.Contains(value as string))
        {
            return ValidationResult.Success;
        }

        var memberName = validationContext.MemberName;

        return new ValidationResult(
            CurrencyErrors.Unsupported(value as string).Message,
            memberName is null ? null : [memberName]);
    }
}

internal static class CurrencyErrors
{
    public static Error Unsupported(string? currency) =>
        new("Validation.UnsupportedCurrency",
            $"Currency '{currency}' is not supported. Accepted currencies: {SupportedCurrencies.Accepted}.");
}
