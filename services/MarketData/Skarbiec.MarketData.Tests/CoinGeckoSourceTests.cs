using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

// The recorded responses are real api.coingecko.com bodies; coingecko-malformed.json is the 404 body of an invalid path.
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
        Assert.Equal(1, client.LatestRequestCount);
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
        Assert.Equal(2, client.LatestRequestCount);
        var delay = Assert.Single(capturedDelays);
        Assert.Equal(TimeSpan.FromSeconds(54), delay);
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
        Assert.Equal(2, client.LatestRequestCount);
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
        Assert.Equal(5, result.Values.Count);
        Assert.All(result.Values, v => Assert.Equal(Bitcoin.Id, v.InstrumentId));
        Assert.Equal(new DateOnly(2026, 4, 20), result.Values[0].Date);
        Assert.Equal(75953.47314954984m, result.Values[0].Close);
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

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Equal(2, client.HistoryRequestCount);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(capturedDelays));
    }
}
