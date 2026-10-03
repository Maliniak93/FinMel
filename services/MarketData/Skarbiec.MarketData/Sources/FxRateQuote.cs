namespace Skarbiec.MarketData.Sources;

public sealed record FxRateQuote(string Pair, DateOnly Date, decimal Rate);
