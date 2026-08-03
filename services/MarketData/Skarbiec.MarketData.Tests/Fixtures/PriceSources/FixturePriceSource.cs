using System.Text.Json;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>
/// Reference <see cref="IPriceSource"/> driven by a recorded raw response instead of a live HTTP
/// call — proves the tri-state outcome contract (T2.2 AC) end-to-end before any real vendor client
/// exists. Real sources (T2.3-T2.5) follow the same fetch-then-parse split: a thin HTTP fetch, and a
/// pure parse method exercised the same way against their own recorded responses.
/// </summary>
public sealed class FixturePriceSource(PriceSource source, string rawResponse) : IPriceSource
{
    public PriceSource Source { get; } = source;

    public TimeSpan RequestDelay => TimeSpan.Zero;

    public Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken) =>
        Task.FromResult(Parse(rawResponse, instruments));

    public Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(Parse(rawResponse, [instrument]));

    private static PriceFetchResult<InstrumentQuote> Parse(
        string raw, IReadOnlyCollection<Instrument> instruments)
    {
        FixtureQuote[]? quotes;
        try
        {
            quotes = JsonSerializer.Deserialize<FixtureQuote[]>(raw);
        }
        catch (JsonException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"malformed payload: {ex.Message}");
        }

        if (quotes is null or { Length: 0 })
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        var instrumentByTicker = instruments.ToDictionary(i => i.Ticker);
        var values = quotes
            .Where(q => instrumentByTicker.ContainsKey(q.Ticker))
            .Select(q => new InstrumentQuote(instrumentByTicker[q.Ticker].Id, q.Date, q.Close))
            .ToList();

        return PriceFetchResult<InstrumentQuote>.Success(values);
    }

    private sealed record FixtureQuote(string Ticker, DateOnly Date, decimal Close);
}
