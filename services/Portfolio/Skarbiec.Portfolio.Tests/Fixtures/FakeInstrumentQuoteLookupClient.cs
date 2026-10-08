using System.Collections.Concurrent;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

public sealed class FakeInstrumentQuoteLookupClient : IInstrumentQuoteLookupClient
{
    private readonly ConcurrentDictionary<Guid, InstrumentQuote> _instruments = new();
    private readonly ConcurrentDictionary<string, decimal> _rates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<IReadOnlyList<Guid>> _calls = new();
    private volatile bool _unavailable;

    public IReadOnlyList<IReadOnlyList<Guid>> Calls => [.. _calls];

    public FakeInstrumentQuoteLookupClient WithInstrument(
        Guid instrumentId,
        string ticker,
        string quoteCurrency,
        decimal? lastPrice,
        DateOnly? lastPriceDate = null,
        string? exchange = null,
        string? name = null)
    {
        _instruments[instrumentId] = new InstrumentQuote(
            instrumentId, ticker, name ?? ticker, exchange, quoteCurrency, lastPrice, lastPrice is null ? null : lastPriceDate ?? new DateOnly(2026, 1, 30));
        return this;
    }

    // Keyed by the quote currency; the rate is {currency}PLN.
    public FakeInstrumentQuoteLookupClient WithRate(string currency, decimal rate)
    {
        _rates[currency] = rate;
        return this;
    }

    public FakeInstrumentQuoteLookupClient WithUnavailable()
    {
        _unavailable = true;
        return this;
    }

    public Task<InstrumentQuoteLookupResult> GetQuotesAsync(
        IReadOnlyCollection<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        _calls.Enqueue([.. instrumentIds]);

        if (_unavailable)
        {
            return Task.FromResult(InstrumentQuoteLookupResult.Unavailable);
        }

        var found = instrumentIds
            .Where(_instruments.ContainsKey)
            .ToDictionary(id => id, id => _instruments[id]);
        IReadOnlyDictionary<string, decimal> rates = new Dictionary<string, decimal>(_rates, StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(InstrumentQuoteLookupResult.Found(found, rates));
    }
}
