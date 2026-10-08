using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Yahoo;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

public sealed class YahooInstrumentSearchTests
{
    [Fact]
    public async Task Filters_ConfiguredExchanges_AndTypes()
    {
        var client = new FakeYahooApiClient().WithSearchResponse(RecordedResponse.Read("yahoo-search-vwce.json"));
        var search = new YahooInstrumentSearch(client, Options.Create(new InstrumentSearchOptions()), NullLogger<YahooInstrumentSearch>.Instance);

        var outcome = await search.SearchAsync("vwce", TestContext.Current.CancellationToken);

        Assert.False(outcome.IsUnavailable);
        var candidate = Assert.Single(outcome.Candidates);
        Assert.Equal("VWCE.DE", candidate.Ticker);
        Assert.Equal("Vanguard FTSE All-World UCITS ETF", candidate.Name);
        Assert.Equal(AssetClass.Etf, candidate.AssetClass);
        Assert.Equal("Xetra", candidate.Exchange);
        Assert.Equal("EUR", candidate.QuoteCurrency);
        Assert.Equal(["vwce"], client.SearchQueries);
    }
}
