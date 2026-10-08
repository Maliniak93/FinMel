namespace Skarbiec.Portfolio.MarketData;

public enum InstrumentQuoteLookupStatus
{
    Found,
    Unavailable,
}

/// <summary>LastPrice is the latest close on or before the lookup date, in QuoteCurrency; both last-price fields are null when there is none.</summary>
public sealed record InstrumentQuote(
    Guid InstrumentId,
    string Ticker,
    string Name,
    string? Exchange,
    string QuoteCurrency,
    decimal? LastPrice,
    DateOnly? LastPriceDate);

/// <summary>Rates holds {currency}PLN keyed by the currency code; an instrument MarketData does not know is absent from Instruments.</summary>
public sealed record InstrumentQuoteLookupResult(
    InstrumentQuoteLookupStatus Status,
    IReadOnlyDictionary<Guid, InstrumentQuote>? Instruments,
    IReadOnlyDictionary<string, decimal>? Rates)
{
    public static InstrumentQuoteLookupResult Unavailable { get; } = new(InstrumentQuoteLookupStatus.Unavailable, null, null);

    public static InstrumentQuoteLookupResult Found(
        IReadOnlyDictionary<Guid, InstrumentQuote> instruments, IReadOnlyDictionary<string, decimal> rates) =>
        new(InstrumentQuoteLookupStatus.Found, instruments, rates);
}

public interface IInstrumentQuoteLookupClient
{
    Task<InstrumentQuoteLookupResult> GetQuotesAsync(
        IReadOnlyCollection<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken);
}
