using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Fixture-based tests for the real CoinGecko source (T2.5 AC: "batch price parse, history parse,
/// 429 → retry-after respected (simulated)", "rate limiting verified... no 429 storm on a
/// 10-instrument batch"), via <see cref="FakeCoinGeckoApiClient"/> — same test-kit pattern as
/// T2.3/T2.4's <c>NbpSourceTests</c>/<c>StooqSourceTests</c>. <c>coingecko-latest-happy-path.json</c>,
/// <c>coingecko-latest-no-data.json</c>, <c>coingecko-history-happy-path.json</c> and
/// <c>coingecko-history-no-data.json</c> are real responses recorded live from api.coingecko.com on
/// 2026-08-03; <c>coingecko-malformed.json</c> is a real 404 body from an invalid path on the same
/// host (an unexpected-but-valid-JSON shape, not the batch/history object either parser expects).
/// </summary>
public sealed class CoinGeckoSourceTests
{
    private static readonly Instrument Bitcoin = new()
    {
        Id = Guid.NewGuid(),
        Ticker = "bitcoin",
        Name = "Bitcoin",
        Source = PriceSource.CoinGecko,
        QuoteCurrency = "USD",
        AssetClass = AssetClass.Crypto,
    };

    private static readonly Instrument Ethereum = new()
    {
        Id = Guid.NewGuid(),
        Ticker = "ethereum",
        Name = "Ethereum",
        Source = PriceSource.CoinGecko,
        QuoteCurrency = "USD",
        AssetClass = AssetClass.Crypto,
    };

    private static CoinGeckoPriceSource CreateSource(FakeCoinGeckoApiClient client) =>
        new(client, NullLogger<CoinGeckoPriceSource>.Instance);

    [Fact]
    public async Task FetchLatestAsync_HappyPath_ParsesBatchedResponse()
    {
        var client = new FakeCoinGeckoApiClient().WithLatestResponse(RecordedResponse.Read("coingecko-latest-happy-path.json"));
        var source = CreateSource(client);

        var result = await source.FetchLatestAsync([Bitcoin, Ethereum], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Values.Count);
        Assert.Equal(1, client.LatestRequestCount); // one call for both coins, not one per coin
        var bitcoin = Assert.Single(result.Values, v => v.InstrumentId == Bitcoin.Id);
        Assert.Equal(new DateOnly(2026, 8, 3), bitcoin.Date);
        Assert.Equal(63890m, bitcoin.Close);
        var ethereum = Assert.Single(result.Values, v => v.InstrumentId == Ethereum.Id);
        Assert.Equal(1870.65m, ethereum.Close);
    }

    [Fact]
    public async Task FetchLatestAsync_UnknownId_ReturnsNoData_NotError()
    {
        var unknownInstrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "not-a-real-coin-id-xyz",
            Name = "Unknown Coin",
            Source = PriceSource.CoinGecko,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Crypto,
        };
        var client = new FakeCoinGeckoApiClient().WithLatestResponse(RecordedResponse.Read("coingecko-latest-no-data.json"));
        var source = CreateSource(client);

        var result = await source.FetchLatestAsync([unknownInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchLatestAsync_MalformedPayload_ReturnsErrorResult_DoesNotThrow()
    {
        var client = new FakeCoinGeckoApiClient().WithLatestResponse(RecordedResponse.Read("coingecko-malformed.json"));
        var source = CreateSource(client);

        var result = await source.FetchLatestAsync([Bitcoin], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchLatestAsync_TransportFailure_ReturnsErrorResult_DoesNotThrow()
    {
        var client = new FakeCoinGeckoApiClient().ThrowingOnLatest(new HttpRequestException("simulated network failure"));
        var source = CreateSource(client);

        var result = await source.FetchLatestAsync([Bitcoin], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task FetchLatestAsync_TenInstrumentBatch_MakesExactlyOneRequest_NoStorm()
    {
        var instruments = Enumerable.Range(0, 10)
            .Select(i => new Instrument
            {
                Id = Guid.NewGuid(),
                Ticker = $"coin-{i}",
                Name = $"Coin {i}",
                Source = PriceSource.CoinGecko,
                QuoteCurrency = "USD",
                AssetClass = AssetClass.Crypto,
            })
            .ToArray();
        var client = new FakeCoinGeckoApiClient().WithLatestResponse(RecordedResponse.Read("coingecko-latest-no-data.json"));
        var source = CreateSource(client);

        await source.FetchLatestAsync(instruments, TestContext.Current.CancellationToken);

        Assert.Equal(1, client.LatestRequestCount);
        Assert.Equal(10, client.LastRequestedIds!.Count);
    }

    [Fact]
    public async Task FetchLatestAsync_RateLimitedOnce_WaitsRetryAfterThenSucceeds()
    {
        var capturedDelays = new List<TimeSpan>();
        var client = new FakeCoinGeckoApiClient().RateLimitedOnceThenLatest(
            TimeSpan.FromSeconds(54), RecordedResponse.Read("coingecko-latest-happy-path.json"));
        var source = new CoinGeckoPriceSource(
            client, NullLogger<CoinGeckoPriceSource>.Instance,
            (delay, _) => { capturedDelays.Add(delay); return Task.CompletedTask; });

        var result = await source.FetchLatestAsync([Bitcoin, Ethereum], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Values.Count);
        Assert.Equal(2, client.LatestRequestCount); // the rate-limited call, then the retry
        var delay = Assert.Single(capturedDelays);
        Assert.Equal(TimeSpan.FromSeconds(54), delay); // Retry-After respected exactly
    }

    [Fact]
    public async Task FetchLatestAsync_RateLimitedTwiceInARow_ReturnsErrorResult_DoesNotThrow()
    {
        var capturedDelays = new List<TimeSpan>();
        var client = new FakeCoinGeckoApiClient().AlwaysRateLimitedOnLatest(TimeSpan.FromSeconds(30));
        var source = new CoinGeckoPriceSource(
            client, NullLogger<CoinGeckoPriceSource>.Instance,
            (delay, _) => { capturedDelays.Add(delay); return Task.CompletedTask; });

        var result = await source.FetchLatestAsync([Bitcoin], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
        Assert.Equal(2, client.LatestRequestCount); // one retry attempted, then gives up — never hammers
        Assert.Single(capturedDelays);
    }

    [Fact]
    public async Task FetchHistoryAsync_HappyPath_ReturnsOneQuotePerDate_DedupedFromFinerGranularity()
    {
        var client = new FakeCoinGeckoApiClient().WithHistoryResponse(
            Bitcoin.Ticker, RecordedResponse.Read("coingecko-history-happy-path.json"));
        var source = CreateSource(client);
        var from = new DateOnly(2026, 4, 20);
        var to = new DateOnly(2026, 4, 24);

        var result = await source.FetchHistoryAsync(Bitcoin, from, to, TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(5, result.Values.Count); // 97 hourly points collapse to 5 distinct UTC dates
        Assert.All(result.Values, v => Assert.Equal(Bitcoin.Id, v.InstrumentId));
        Assert.Equal(new DateOnly(2026, 4, 20), result.Values[0].Date);
        Assert.Equal(75953.47314954984m, result.Values[0].Close); // last point of 04-20, not the first
        Assert.Equal(new DateOnly(2026, 4, 24), result.Values[^1].Date);
        Assert.Equal(78275.32582745969m, result.Values[^1].Close);
        Assert.Equal(1, client.HistoryRequestCount);
    }

    [Fact]
    public async Task FetchHistoryAsync_NoData_ReturnsNoData_NotError()
    {
        var client = new FakeCoinGeckoApiClient().WithHistoryResponse(
            Bitcoin.Ticker, RecordedResponse.Read("coingecko-history-no-data.json"));
        var source = CreateSource(client);

        var result = await source.FetchHistoryAsync(
            Bitcoin, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12), TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchHistoryAsync_MalformedPayload_ReturnsErrorResult_DoesNotThrow()
    {
        var client = new FakeCoinGeckoApiClient().WithHistoryResponse(
            Bitcoin.Ticker, RecordedResponse.Read("coingecko-malformed.json"));
        var source = CreateSource(client);

        var result = await source.FetchHistoryAsync(
            Bitcoin, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task FetchHistoryAsync_RateLimitedOnce_WaitsRetryAfterThenSucceeds()
    {
        var capturedDelays = new List<TimeSpan>();
        var client = new FakeCoinGeckoApiClient().RateLimitedOnceThenHistory(
            Bitcoin.Ticker, TimeSpan.FromSeconds(20), RecordedResponse.Read("coingecko-history-no-data.json"));
        var source = new CoinGeckoPriceSource(
            client, NullLogger<CoinGeckoPriceSource>.Instance,
            (delay, _) => { capturedDelays.Add(delay); return Task.CompletedTask; });

        var result = await source.FetchHistoryAsync(
            Bitcoin, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2), TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome); // reached the parser at all proves the retry succeeded
        Assert.Equal(2, client.HistoryRequestCount);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(capturedDelays));
    }
}
