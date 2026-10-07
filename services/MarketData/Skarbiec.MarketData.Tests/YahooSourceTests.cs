using System.Net;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Yahoo;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

public sealed class YahooSourceTests
{
    private static readonly Instrument PlnStock = new()
    {
        Id = Guid.NewGuid(),
        Ticker = "CDR.WA",
        Name = "CD Projekt",
        Source = PriceSource.Yahoo,
        QuoteCurrency = "PLN",
        AssetClass = AssetClass.Stock,
    };

    [Fact]
    public async Task Latest_SkipsNullClose_UsesExchangeDate()
    {
        var client = new FakeYahooApiClient()
            .WithResponse(PlnStock.Ticker, RecordedResponse.Read("yahoo-latest-null-close.json"));
        var source = new YahooPriceSource(client);

        var result = await source.FetchLatestAsync([PlnStock], TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        var quote = Assert.Single(result.Values);
        Assert.Equal(PlnStock.Id, quote.InstrumentId);
        Assert.Equal(new DateOnly(2026, 10, 7), quote.Date);
        Assert.Equal(190.4m, quote.Close);
    }

    [Fact]
    public async Task History_ReturnsDailyCloses()
    {
        var client = new FakeYahooApiClient()
            .WithResponse(PlnStock.Ticker, RecordedResponse.Read("yahoo-history-happy-path.json"));
        var source = new YahooPriceSource(client);
        var from = new DateOnly(2026, 9, 21);
        var to = new DateOnly(2026, 10, 2);

        var result = await source.FetchHistoryAsync(PlnStock, from, to, TestContext.Current.CancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.Equal(10, result.Values.Count);
        Assert.All(result.Values, v =>
        {
            Assert.Equal(PlnStock.Id, v.InstrumentId);
            Assert.InRange(v.Date, from, to);
        });
        Assert.Equal(10, result.Values.Select(v => v.Date).Distinct().Count());
        Assert.Equal(new DateOnly(2026, 9, 21), result.Values[0].Date);
        Assert.Equal(180m, result.Values[0].Close);
        Assert.Equal(new DateOnly(2026, 10, 2), result.Values[^1].Date);
        Assert.Equal(191.25m, result.Values[^1].Close);
    }

    [Fact]
    public async Task Fetch_MapsFailures()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var from = new DateOnly(2026, 9, 21);
        var to = new DateOnly(2026, 10, 2);

        var notFound = new YahooPriceSource(new FakeYahooApiClient()
            .WithResponse(PlnStock.Ticker, RecordedResponse.Read("yahoo-not-found.json")));
        var serverError = new YahooPriceSource(new FakeYahooApiClient()
            .ThrowingFor(PlnStock.Ticker, new HttpRequestException("boom", null, HttpStatusCode.InternalServerError)));
        var transport = new YahooPriceSource(new FakeYahooApiClient()
            .ThrowingFor(PlnStock.Ticker, new HttpRequestException("simulated network failure")));

        Assert.Equal(PriceFetchOutcome.NoData, (await notFound.FetchLatestAsync([PlnStock], cancellationToken)).Outcome);
        Assert.Equal(PriceFetchOutcome.NoData, (await notFound.FetchHistoryAsync(PlnStock, from, to, cancellationToken)).Outcome);
        Assert.Equal(PriceFetchOutcome.Error, (await serverError.FetchLatestAsync([PlnStock], cancellationToken)).Outcome);
        Assert.Equal(PriceFetchOutcome.Error, (await serverError.FetchHistoryAsync(PlnStock, from, to, cancellationToken)).Outcome);
        Assert.Equal(PriceFetchOutcome.Error, (await transport.FetchLatestAsync([PlnStock], cancellationToken)).Outcome);
        Assert.Equal(PriceFetchOutcome.Error, (await transport.FetchHistoryAsync(PlnStock, from, to, cancellationToken)).Outcome);
    }
}
