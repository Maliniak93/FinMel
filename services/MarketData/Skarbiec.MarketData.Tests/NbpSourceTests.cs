using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Nbp;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Fixture-based tests for the real NBP source (T2.3 AC: "table A parse, gold parse, holiday (no
/// data), API error"), via <see cref="FakeNbpApiClient"/> — same test-kit pattern as T2.2's
/// <see cref="PriceSourceAbstractionTests"/>, one level lower (swapping the HTTP fetch instead of
/// the whole source).
/// </summary>
public sealed class NbpSourceTests
{
    private static readonly Instrument GoldInstrument = new()
    {
        Id = Guid.NewGuid(),
        Ticker = "XAU",
        Name = "Gold (1 gram, NBP)",
        Source = PriceSource.Nbp,
        QuoteCurrency = "PLN",
        AssetClass = AssetClass.PreciousMetal,
    };

    [Fact]
    public async Task FxRateSource_FetchLatestAsync_HappyPath_ParsesTableA()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-table-a-happy-path.json"));
        var source = new NbpFxRateSource(client);

        var result = await source.FetchLatestAsync(["USD", "EUR"], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Values.Count);
        var usd = Assert.Single(result.Values, v => v.Pair == "USDPLN");
        Assert.Equal(new DateOnly(2026, 8, 3), usd.Date);
        Assert.Equal(3.7330m, usd.Rate);
        var eur = Assert.Single(result.Values, v => v.Pair == "EURPLN");
        Assert.Equal(4.3013m, eur.Rate);
    }

    [Fact]
    public async Task FxRateSource_FetchLatestAsync_IgnoresCurrenciesNotRequested()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-table-a-happy-path.json"));
        var source = new NbpFxRateSource(client);

        var result = await source.FetchLatestAsync(["USD"], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        var quote = Assert.Single(result.Values);
        Assert.Equal("USDPLN", quote.Pair);
    }

    [Fact]
    public async Task PriceSource_FetchLatestAsync_HappyPath_ParsesGold()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-gold-happy-path.json"));
        var source = new NbpPriceSource(client);

        var result = await source.FetchLatestAsync([GoldInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        var quote = Assert.Single(result.Values);
        Assert.Equal(GoldInstrument.Id, quote.InstrumentId);
        Assert.Equal(new DateOnly(2026, 8, 3), quote.Date);
        Assert.Equal(488.51m, quote.Close);
    }

    [Fact]
    public async Task FxRateSource_FetchLatestAsync_Holiday_ReturnsNoData_NotError()
    {
        var client = FakeNbpApiClient.NoData();
        var source = new NbpFxRateSource(client);

        var result = await source.FetchLatestAsync(["USD"], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task PriceSource_FetchLatestAsync_Holiday_ReturnsNoData_NotError()
    {
        var client = FakeNbpApiClient.NoData();
        var source = new NbpPriceSource(client);

        var result = await source.FetchLatestAsync([GoldInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FxRateSource_FetchLatestAsync_MalformedPayload_ReturnsErrorResult_DoesNotThrow()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-malformed.json"));
        var source = new NbpFxRateSource(client);

        var result = await source.FetchLatestAsync(["USD"], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task PriceSource_FetchLatestAsync_MalformedPayload_ReturnsErrorResult_DoesNotThrow()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-malformed.json"));
        var source = new NbpPriceSource(client);

        var result = await source.FetchLatestAsync([GoldInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task FxRateSource_FetchLatestAsync_TransportFailure_ReturnsErrorResult_DoesNotThrow()
    {
        var client = FakeNbpApiClient.ThrowingOnRequest(new HttpRequestException("simulated network failure"));
        var source = new NbpFxRateSource(client);

        var result = await source.FetchLatestAsync(["USD"], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task PriceSource_FetchLatestAsync_TransportFailure_ReturnsErrorResult_DoesNotThrow()
    {
        var client = FakeNbpApiClient.ThrowingOnRequest(new HttpRequestException("simulated network failure"));
        var source = new NbpPriceSource(client);

        var result = await source.FetchLatestAsync([GoldInstrument], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task FxRateSource_FetchHistoryAsync_RangeOver93Days_ChunksIntoMultipleRequests()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-table-a-happy-path.json"));
        var source = new NbpFxRateSource(client);
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(199); // 200-day span > NBP's 93-day table A limit

        var result = await source.FetchHistoryAsync("USD", from, to, TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(3, client.RangeRequestCount); // ceil(200/93)
    }

    [Fact]
    public async Task PriceSource_FetchHistoryAsync_RangeOver367Days_ChunksIntoMultipleRequests()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-gold-happy-path.json"));
        var source = new NbpPriceSource(client);
        var from = new DateOnly(2024, 1, 1);
        var to = from.AddDays(399); // 400-day span > this source's 367-day chunk size

        var result = await source.FetchHistoryAsync(GoldInstrument, from, to, TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(2, client.RangeRequestCount); // ceil(400/367)
    }
}
