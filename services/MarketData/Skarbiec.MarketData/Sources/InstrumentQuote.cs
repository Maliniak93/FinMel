namespace Skarbiec.MarketData.Sources;

public sealed record InstrumentQuote(Guid InstrumentId, DateOnly Date, decimal Close);
