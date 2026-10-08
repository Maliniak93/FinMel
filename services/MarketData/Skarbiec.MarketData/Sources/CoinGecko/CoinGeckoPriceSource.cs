using System.Text.Json;
using System.Text.Json.Serialization;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.CoinGecko;

// Ticker holds CoinGecko's coin id (bitcoin), not the trading symbol.
public sealed class CoinGeckoPriceSource : IPriceSource
{
    // The free tier answered 429 after a few quick calls; only per-coin history calls need pacing, as latest prices are one batched call.
    public static readonly TimeSpan DefaultRequestDelay = TimeSpan.FromSeconds(2);

    private readonly ICoinGeckoApiClient _client;
    private readonly ILogger<CoinGeckoPriceSource> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public CoinGeckoPriceSource(ICoinGeckoApiClient client, ILogger<CoinGeckoPriceSource> logger)
        : this(client, logger, Task.Delay)
    {
    }

    // Test seam: a test captures the backoff wait instead of sleeping.
    public CoinGeckoPriceSource(
        ICoinGeckoApiClient client, ILogger<CoinGeckoPriceSource> logger, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _client = client;
        _logger = logger;
        _delay = delay;
    }

    public PriceSource Source => PriceSource.CoinGecko;

    public TimeSpan RequestDelay => DefaultRequestDelay;

    // The free tier serves only the last 365 days of history.
    public int? MaxHistoryDays => 365;

    public async Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        var ids = instruments.Select(i => i.Ticker).ToArray();
        if (ids.Length == 0)
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        string raw;
        try
        {
            raw = await FetchWithRateLimitRetryAsync(() => _client.GetLatestAsync(ids, cancellationToken), cancellationToken);
        }
        catch (CoinGeckoRateLimitedException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"CoinGecko latest-price request rate-limited twice in a row: {ex.Message}");
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return PriceFetchResult<InstrumentQuote>.Error($"CoinGecko latest-price request failed: {ex.Message}");
        }

        _logger.LogDebug("CoinGecko latest-price fetch: batched {InstrumentCount} instrument ids into one request.", ids.Length);

        return ParseLatest(raw, instruments);
    }

    public async Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        string raw;
        try
        {
            raw = await FetchWithRateLimitRetryAsync(
                () => _client.GetHistoryAsync(instrument.Ticker, from, to, cancellationToken), cancellationToken);
        }
        catch (CoinGeckoRateLimitedException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error(
                $"CoinGecko history request for {instrument.Ticker} rate-limited twice in a row: {ex.Message}");
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return PriceFetchResult<InstrumentQuote>.Error($"CoinGecko history request for {instrument.Ticker} failed: {ex.Message}");
        }

        return ParseHistory(raw, instrument.Id);
    }

    // One retry after Retry-After rides out a single rate-limit hit; a source that stays limited still ends in Error.
    private async Task<string> FetchWithRateLimitRetryAsync(Func<Task<string>> fetch, CancellationToken cancellationToken)
    {
        try
        {
            return await fetch();
        }
        catch (CoinGeckoRateLimitedException ex)
        {
            _logger.LogWarning(
                "CoinGecko rate limit (429) hit; pacing by waiting {RetryAfter} before the single retry.", ex.RetryAfter);
            await _delay(ex.RetryAfter, cancellationToken);
            return await fetch();
        }
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    // {"<id>":{"usd":<price>,"last_updated_at":<unix seconds>}}; an unrecognised id is missing entirely.
    private static PriceFetchResult<InstrumentQuote> ParseLatest(string raw, IReadOnlyCollection<Instrument> instruments)
    {
        Dictionary<string, CoinEntryDto>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<Dictionary<string, CoinEntryDto>>(raw);
        }
        catch (JsonException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"malformed CoinGecko latest-price payload: {ex.Message}");
        }

        if (entries is null || entries.Count == 0)
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        var values = new List<InstrumentQuote>();
        foreach (var instrument in instruments)
        {
            // An unrecognised id is excluded instead of failing the whole batch.
            if (!entries.TryGetValue(instrument.Ticker, out var entry) || entry.Usd is null || entry.LastUpdatedAt is null)
            {
                continue;
            }

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(entry.LastUpdatedAt.Value).UtcDateTime);
            values.Add(new InstrumentQuote(instrument.Id, date, entry.Usd.Value));
        }

        return values.Count > 0 ? PriceFetchResult<InstrumentQuote>.Success(values) : PriceFetchResult<InstrumentQuote>.NoData();
    }

    // {"prices":[[<unix ms>,<price>]]}: granularity varies with range age, so each day keeps its last point; a missing "prices" is malformed.
    private static PriceFetchResult<InstrumentQuote> ParseHistory(string raw, Guid instrumentId)
    {
        MarketChartDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<MarketChartDto>(raw);
        }
        catch (JsonException ex)
        {
            return PriceFetchResult<InstrumentQuote>.Error($"malformed CoinGecko history payload: {ex.Message}");
        }

        if (dto is null || dto.Prices.Count == 0)
        {
            return PriceFetchResult<InstrumentQuote>.NoData();
        }

        var byDate = new SortedDictionary<DateOnly, decimal>();
        foreach (var point in dto.Prices)
        {
            if (point.Length != 2)
            {
                return PriceFetchResult<InstrumentQuote>.Error("malformed CoinGecko history payload: expected [timestamp, price] pairs");
            }

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds((long)point[0]).UtcDateTime);
            byDate[date] = point[1];
        }

        var values = byDate.Select(kv => new InstrumentQuote(instrumentId, kv.Key, kv.Value)).ToList();
        return PriceFetchResult<InstrumentQuote>.Success(values);
    }

    private sealed record CoinEntryDto(
        [property: JsonPropertyName("usd")] decimal? Usd,
        [property: JsonPropertyName("last_updated_at")] long? LastUpdatedAt);

    private sealed record MarketChartDto
    {
        [JsonPropertyName("prices")]
        public required List<decimal[]> Prices { get; init; }
    }
}
