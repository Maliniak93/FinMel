using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Contracts.Tests;

public sealed class SupportedCurrenciesTests
{
    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public void Validate_WithSupportedCurrency_Succeeds(string currency)
    {
        Assert.True(SupportedCurrencies.Validate(currency).IsSuccess);
    }

    [Theory]
    [InlineData("GBP")] // seeded as an FxRate pair in MarketData, still not user-selectable
    [InlineData("CHF")]
    [InlineData("XYZ")]
    [InlineData("pln")] // codes are canonical uppercase, never normalised
    [InlineData("")]
    [InlineData(null)]
    public void Validate_WithUnsupportedCurrency_FailsNamingTheAcceptedSet(string? currency)
    {
        var result = SupportedCurrencies.Validate(currency);

        Assert.True(result.IsFailure);
        Assert.Equal("Validation.UnsupportedCurrency", result.Error.Code);
        Assert.Contains("PLN, EUR, USD", result.Error.Message);
    }

    [Fact]
    public void Default_IsPln_AndIsItselfSupported()
    {
        Assert.Equal("PLN", SupportedCurrencies.Default);
        Assert.Equal(Money.BaseCurrency, SupportedCurrencies.Default);
        Assert.True(SupportedCurrencies.Contains(SupportedCurrencies.Default));
    }

    [Fact]
    public void All_HoldsExactlyTheThreeSupportedCodes()
    {
        Assert.Equal(["PLN", "EUR", "USD"], SupportedCurrencies.All);
    }

    [Fact]
    public void Attribute_OnUnsupportedCurrency_ReportsTheMemberAndTheAcceptedSet()
    {
        var subject = new CurrencyHolder { Currency = "GBP" };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            subject, new ValidationContext(subject), results, validateAllProperties: true);

        Assert.False(isValid);
        var failure = Assert.Single(results);
        Assert.Equal([nameof(CurrencyHolder.Currency)], failure.MemberNames);
        Assert.Contains("PLN, EUR, USD", failure.ErrorMessage);
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData(null)] // [Required] owns null, not this attribute
    public void Attribute_OnSupportedOrMissingCurrency_Passes(string? currency)
    {
        var subject = new CurrencyHolder { Currency = currency };
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            subject, new ValidationContext(subject), results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    private sealed record CurrencyHolder
    {
        [SupportedCurrency]
        public string? Currency { get; init; }
    }
}
