using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.MarketData;

/// <summary>Narrow interface so tests can substitute a fake instead of standing up MarketData's own Testcontainer host (mirrors Portfolio's own <c>IInstrumentLookupClient</c>, T2.9).</summary>
public interface IPriceQuoteClient
{
    Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> GetLatestPricesAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, FxRateLookup>> GetLatestFxRatesAsync(
        IReadOnlyList<string> pairs, DateOnly asOfDate, CancellationToken cancellationToken);
}
