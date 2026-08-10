using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Tests.Fixtures;

/// <summary>Stands in for <see cref="IPriceQuoteClient"/> — see <see cref="FakePositionsClient"/> for why.</summary>
public sealed class FakePriceQuoteClient : IPriceQuoteClient
{
    private readonly Dictionary<Guid, InstrumentPriceLookup> _prices = [];
    private readonly Dictionary<string, FxRateLookup> _fxRates = new(StringComparer.OrdinalIgnoreCase);

    public FakePriceQuoteClient WithPrice(Guid instrumentId, InstrumentPriceLookup price)
    {
        _prices[instrumentId] = price;
        return this;
    }

    public FakePriceQuoteClient WithFxRate(string pair, FxRateLookup rate)
    {
        _fxRates[pair] = rate;
        return this;
    }

    public Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> GetLatestPricesAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, InstrumentPriceLookup>>(
            _prices.Where(p => instrumentIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));

    public Task<IReadOnlyDictionary<string, FxRateLookup>> GetLatestFxRatesAsync(
        IReadOnlyList<string> pairs, DateOnly asOfDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, FxRateLookup>>(
            _fxRates.Where(r => pairs.Contains(r.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase));
}
