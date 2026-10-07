using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.GoldApi;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

public sealed class GoldApiSourceTests
{
    private static readonly Instrument Silver = new()
    {
        Id = MetalInstruments.InstrumentIdFor(Metal.Silver),
        Ticker = "XAG",
        Name = "Silver (1 g)",
        Source = PriceSource.GoldApi,
        QuoteCurrency = "USD",
        AssetClass = AssetClass.PreciousMetal,
    };

    private static GoldApiPriceSource CreateSource(FakeGoldApiClient client) =>
        new(client, NullLogger<GoldApiPriceSource>.Instance);

    [Fact]
    public async Task FetchLatest_ConvertsOunceToGram()
    {
        var client = new FakeGoldApiClient().WithResponse("XAG", RecordedResponse.Read("gold-api-xag.json"));
        var source = CreateSource(client);

        var result = await source.FetchLatestAsync([Silver], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        var quote = Assert.Single(result.Values);
        Assert.Equal(Silver.Id, quote.InstrumentId);
        Assert.Equal(new DateOnly(2026, 10, 6), quote.Date);
        Assert.Equal(Math.Round(61.525002m / 31.1034768m, 8), quote.Close);
    }

    [Fact]
    public async Task FetchLatest_TransportFailure_IsError()
    {
        var client = new FakeGoldApiClient().ThrowingOnRequest(new HttpRequestException("simulated network failure"));
        var source = CreateSource(client);

        var result = await source.FetchLatestAsync([Silver], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Error, result.Outcome);
        Assert.NotNull(result.ErrorReason);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchLatest_UnknownSymbol_IsNoData()
    {
        var client = new FakeGoldApiClient();
        var source = CreateSource(client);

        var result = await source.FetchLatestAsync([Silver], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.NoData, result.Outcome);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FetchHistory_ReturnsLatestQuoteInsideRange()
    {
        var client = new FakeGoldApiClient().WithResponse("XAG", RecordedResponse.Read("gold-api-xag.json"));
        var source = CreateSource(client);
        var cancellationToken = TestContext.Current.CancellationToken;

        var inside = await source.FetchHistoryAsync(Silver, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 6), cancellationToken);
        var outside = await source.FetchHistoryAsync(Silver, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 5), cancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, inside.Outcome);
        var quote = Assert.Single(inside.Values);
        Assert.Equal(new DateOnly(2026, 10, 6), quote.Date);
        Assert.Equal(Math.Round(61.525002m / 31.1034768m, 8), quote.Close);
        Assert.Equal(PriceFetchOutcome.NoData, outside.Outcome);
        Assert.Empty(outside.Values);
    }
}
