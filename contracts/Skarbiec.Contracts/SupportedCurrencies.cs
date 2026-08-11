using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Contracts;

/// <summary>
/// The currency codes a user is allowed to pick for a portfolio or an asset. The default — and the
/// base currency everything is valued in — is PLN (ADR-008).
/// </summary>
/// <remarks>
/// <para>
/// The restriction covers <b>user-chosen</b> currencies only. MarketData keeps storing whatever its
/// providers quote: <c>MarketDataSeeder</c>'s GBPPLN/CHFPLN rates and a USD instrument's
/// <c>QuoteCurrency</c> stay valid, and narrowing those would break market-asset valuation.
/// </para>
/// <para>
/// Rows written before the rule existed are not rewritten — validation runs on writes only, so an
/// out-of-set currency still reads back unchanged.
/// </para>
/// <para>
/// Codes are matched exactly, in ISO 4217 uppercase: "pln" is rejected rather than normalised, so
/// what reaches a database is always canonical.
/// </para>
/// </remarks>
public static class SupportedCurrencies
{
    /// <summary>Used when a request omits the currency.</summary>
    public const string Default = Money.BaseCurrency;

    /// <summary>The accepted codes, in the order the UI should offer them.</summary>
    public static readonly IReadOnlyList<string> All = [Money.BaseCurrency, "EUR", "USD"];

    /// <summary>The accepted codes as one human-readable string ("PLN, EUR, USD") for error messages.</summary>
    public static string Accepted { get; } = string.Join(", ", All);

    public static bool Contains(string? currency) =>
        currency is not null && All.Contains(currency, StringComparer.Ordinal);

    /// <summary>Returns a failed <see cref="Result"/> instead of throwing (ADR-017).</summary>
    public static Result Validate(string? currency) =>
        Contains(currency) ? Result.Success() : CurrencyErrors.Unsupported(currency);
}

/// <summary>
/// DataAnnotations face of <see cref="SupportedCurrencies"/>, so a request record only needs
/// <c>[SupportedCurrency]</c> and .NET 10's built-in Minimal API validation turns a bad code into a
/// 400 ProblemDetails on its own. <see langword="null"/> passes — <c>[Required]</c> owns that case.
/// </summary>
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
