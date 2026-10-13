using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.MarketData;

public enum InstrumentLookupStatus
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>Every field but Status and Exchange is set only when Status is Found.</summary>
public sealed record InstrumentLookupResult(
    InstrumentLookupStatus Status,
    AssetClass? AssetClass,
    string? QuoteCurrency,
    string? Ticker,
    string? Name,
    string? Exchange)
{
    public static InstrumentLookupResult NotFound { get; } = new(InstrumentLookupStatus.NotFound, null, null, null, null, null);

    public static InstrumentLookupResult Unavailable { get; } = new(InstrumentLookupStatus.Unavailable, null, null, null, null, null);

    public static InstrumentLookupResult Found(
        AssetClass assetClass, string quoteCurrency, string ticker, string name, string? exchange) =>
        new(InstrumentLookupStatus.Found, assetClass, quoteCurrency, ticker, name, exchange);
}

public interface IInstrumentLookupClient
{
    Task<InstrumentLookupResult> CheckAsync(Guid instrumentId, CancellationToken cancellationToken);
}
