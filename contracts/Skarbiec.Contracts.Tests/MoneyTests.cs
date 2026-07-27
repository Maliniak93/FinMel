namespace Skarbiec.Contracts.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Create_WithNegativeAmount_ReturnsFailure()
    {
        var result = Money.Create(-1.00m, "PLN");

        Assert.True(result.IsFailure);
        Assert.Equal("Money.NegativeAmount", result.Error.Code);
    }

    [Fact]
    public void Create_WithMoreThanTwoDecimalPlaces_ReturnsFailure()
    {
        var result = Money.Create(1.005m, "PLN");

        Assert.True(result.IsFailure);
        Assert.Equal("Money.TooManyDecimalPlaces", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutCurrency_ReturnsFailure(string? currency)
    {
        var result = Money.Create(10.00m, currency!);

        Assert.True(result.IsFailure);
        Assert.Equal("Money.CurrencyRequired", result.Error.Code);
    }

    [Fact]
    public void Create_DefaultsToPln()
    {
        var result = Money.Create(10.00m);

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.BaseCurrency, result.Value.Currency);
    }

    [Fact]
    public void Create_WithValidAmountAndCurrency_Succeeds()
    {
        var result = Money.Create(19.99m, "USD");

        Assert.True(result.IsSuccess);
        Assert.Equal(19.99m, result.Value.Amount);
        Assert.Equal("USD", result.Value.Currency);
    }
}
