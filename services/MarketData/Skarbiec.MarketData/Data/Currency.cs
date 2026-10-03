namespace Skarbiec.MarketData.Data;

// Wider than the user-facing SupportedCurrencies; every non-PLN row gets a daily <CODE>PLN rate.
public sealed class Currency
{
    public required string Code { get; init; }

    public required string Name { get; set; }
    public required string Symbol { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public int DisplayOrder { get; set; }
}
