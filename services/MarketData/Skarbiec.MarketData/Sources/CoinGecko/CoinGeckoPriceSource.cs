using System.Text.Json;
using System.Text.Json.Serialization;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.CoinGecko;

/// <summary>
/// <see cref="IPriceSource"/> over CoinGecko's public v3 API — daily crypto prices (E4 [M]).
/// <see cref="Instrument.Ticker"/> stores CoinGecko's own coin id for this source (e.g.
/// <c>bitcoin</c>, <c>ethereum</c> — not the trading symbol), matching the convention
/// <see cref="Data.MarketDataSeeder"/> already seeds. Prices are always fetched in USD (see
/// <see cref="CoinGeckoApiClient"/>'s doc comment); conversion to PLN is a valuation-time concern
/// (ADR-008), not this source's.
/// </summary>
public sealed class CoinGeckoPriceSource : IPriceSource
{
    // Conservative floor between requests for the free (no API key) tier — documented public limits
    // are on the order of a handful of calls per minute, and a live check (T2.5, see
    // CoinGeckoRateLimitedException's doc comment) showed a 429 after only a few calls in quick
    // succession. Only matters for FetchHistoryAsync's per-coin backfill calls (T2.7): FetchLatestAsync
    // batches every instrument into one request regardless of count, so it never needs to pace itself.
    public static readonly TimeSpan DefaultRequestDelay = TimeSpan.FromSeconds(2);

    private readonly ICoinGeckoApiClient _client;
    private readonly ILogger<CoinGeckoPriceSource> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public CoinGeckoPriceSource(ICoinGeckoApiClient client, ILogger<CoinGeckoPriceSource> logger)
        : this(client, logger, Task.Delay)
    {
    }

    /// <summary>Test seam: lets <c>CoinGeckoSourceTests</c> (T2.5 AC: "429 → retry-after respected")
    /// capture/short-circuit the backoff wait instead of the test actually sleeping.</summary>
    public CoinGeckoPriceSource(
        ICoinGeckoApiClient client, ILogger<CoinGeckoPriceSource> logger, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _client = client;
        _logger = logger;
        _delay = delay;
    }

    public PriceSource Source => PriceSource.CoinGecko;

    public TimeSpan RequestDelay => DefaultRequestDelay;

    /// <summary>
    /// Fetches every instrument's latest price in a single batched request (T2.5 scope: "batch by
    /// ids — one call for many coins"), which is also what keeps a many-instrument sync from ever
    /// turning into a 429 storm (T2.5 AC).
    /// </summary>
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

    // One retry after honoring Retry-After — enough to ride out a single free-tier rate-limit hit
    // without turning a transient 429 into a job failure, but bounded so a source that's truly stuck
    // still surfaces as PriceFetchOutcome.Error rather than retrying forever (T2.5 scope: "honor 429
    // with backoff", "never hammer").
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

    // Expected shape: {"<id>":{"usd":<price>,"last_updated_at":<unix seconds>}, ...} — one entry per
    // requested id, missing entirely for an id CoinGecko doesn't recognize (delisted/typo'd ticker).
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
            // An id CoinGecko doesn't recognize is silently excluded rather than failing the whole
            // batch — same contract NbpPriceSource/StooqPriceSource established for an unmatched
            // instrument (T2.3/T2.4).
            if (!entries.TryGetValue(instrument.Ticker, out var entry) || entry.Usd is null || entry.LastUpdatedAt is null)
            {
                continue;
            }

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(entry.LastUpdatedAt.Value).UtcDateTime);
            values.Add(new InstrumentQuote(instrument.Id, date, entry.Usd.Value));
        }

        return values.Count > 0 ? PriceFetchResult<InstrumentQuote>.Success(values) : PriceFetchResult<InstrumentQuote>.NoData();
    }

    // Expected shape: {"prices":[[<unix ms>,<price>], ...]} — granularity varies with range age
    // (5-min/hourly/daily, live-verified 2026-08-03), so points are deduped to one quote per UTC
    // calendar date, keeping the latest point of each day, since PriceQuote is date-only (T2.1).
    // "prices" is a required member: a response missing it entirely (e.g. an unexpected-shape error
    // body) fails deserialization and is reported as malformed, distinct from a genuinely empty
    // "prices":[] for a range with no data.
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
            byDate[date] = point[1]; // points arrive chronologically; last write per date wins
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
