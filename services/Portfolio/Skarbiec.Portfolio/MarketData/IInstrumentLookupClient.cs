using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.MarketData;

public enum InstrumentLookupStatus
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>AssetClass and QuoteCurrency are set only when Status is Found.</summary>
public sealed record InstrumentLookupResult(InstrumentLookupStatus Status, AssetClass? AssetClass, string? QuoteCurrency)
{
    public static InstrumentLookupResult NotFound { get; } = new(InstrumentLookupStatus.NotFound, null, null);

    public static InstrumentLookupResult Unavailable { get; } = new(InstrumentLookupStatus.Unavailable, null, null);

    public static InstrumentLookupResult Found(AssetClass assetClass, string quoteCurrency) =>
        new(InstrumentLookupStatus.Found, assetClass, quoteCurrency);
}

public interface IInstrumentLookupClient
{
    Task<InstrumentLookupResult> CheckAsync(Guid instrumentId, CancellationToken cancellationToken);
}
