using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Nbp;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

public sealed class NbpSourceTests
{
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
    public async Task FxRateSource_FetchLatestAsync_Holiday_ReturnsNoData_NotError()
    {
        var client = FakeNbpApiClient.NoData();
        var source = new NbpFxRateSource(client);

        var result = await source.FetchLatestAsync(["USD"], TestContext.Current.CancellationToken);

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
    public async Task FxRateSource_FetchLatestAsync_TransportFailure_ReturnsErrorResult_DoesNotThrow()
    {
        var client = FakeNbpApiClient.ThrowingOnRequest(new HttpRequestException("simulated network failure"));
        var source = new NbpFxRateSource(client);

        var result = await source.FetchLatestAsync(["USD"], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public async Task FxRateSource_FetchHistoryAsync_RangeOver93Days_ChunksIntoMultipleRequests()
    {
        var client = FakeNbpApiClient.WithResponse(RecordedResponse.Read("nbp-table-a-happy-path.json"));
        var source = new NbpFxRateSource(client);
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(199);

        var result = await source.FetchHistoryAsync("USD", from, to, TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(3, client.RangeRequestCount);
    }
}
