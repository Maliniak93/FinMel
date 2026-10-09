using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.MarketData;

public interface IPriceQuoteClient
{
    Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> GetLatestPricesAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, FxRateLookup>> GetLatestFxRatesAsync(
        IReadOnlyList<string> pairs, DateOnly asOfDate, CancellationToken cancellationToken);

    // Each series ascending: the latest element before from (if any), then every element in from..to.
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<InstrumentPriceLookup>>> GetPriceHistoryAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<FxRateLookup>>> GetFxHistoryAsync(
        IReadOnlyList<string> pairs, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
