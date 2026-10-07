using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Yahoo;

namespace Skarbiec.MarketData.Tests;

public sealed class YahooLiveSmokeTests
{
    [Fact(Skip = "Manual live smoke — hits the real Yahoo Finance API; run explicitly, don't enable in CI.")]
    public async Task LiveYahooApi_CdProjekt_ReturnsAClose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://query1.finance.yahoo.com/") };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        var source = new YahooPriceSource(new YahooApiClient(httpClient));
        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "CDR.WA",
            Name = "CD Projekt",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Stock,
        };

        var result = await source.FetchLatestAsync([instrument], cancellationToken);

        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);
        Assert.True(Assert.Single(result.Values).Close > 0);
    }
}
