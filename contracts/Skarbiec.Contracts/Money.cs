namespace Skarbiec.Contracts;

/// <summary>Amount in a given currency. Base currency is PLN (ADR-008).</summary>
public sealed record Money
{
    public const string BaseCurrency = "PLN";

    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Validates and creates a <see cref="Money"/>; returns a failed <see cref="Result{TValue}"/> instead of throwing (ADR-017).</summary>
    public static Result<Money> Create(decimal amount, string currency = BaseCurrency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return MoneyErrors.CurrencyRequired;
        }

        if (amount < 0)
        {
            return MoneyErrors.NegativeAmount(amount);
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return MoneyErrors.TooManyDecimalPlaces(amount);
        }

        return new Money(amount, currency);
    }
}

internal static class MoneyErrors
{
    public static readonly Error CurrencyRequired = new("Money.CurrencyRequired", "Currency is required.");

    public static Error NegativeAmount(decimal amount) =>
        new("Money.NegativeAmount", $"Amount must not be negative (was {amount}).");

    public static Error TooManyDecimalPlaces(decimal amount) =>
        new("Money.TooManyDecimalPlaces", $"Amount must not have more than 2 decimal places (was {amount}).");
}
