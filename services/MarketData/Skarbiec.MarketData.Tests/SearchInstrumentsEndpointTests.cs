using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// The <100 ms check runs against the handler in SearchInstrumentsPerformanceTests, so HTTP overhead is not measured.
[Collection(TestingDefaults.CollectionName)]
public sealed class SearchInstrumentsEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Search_ByTickerPrefix_ReturnsMatchWithLastPriceAndDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 3), 210.50m, cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 4), 212.00m, cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(SearchInstrumentsUri("AAPL"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = (await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!.Results;
        var match = Assert.Single(results);
        Assert.Equal(instrumentId, match.Id);
        Assert.Equal("AAPL.US", match.Ticker);
        Assert.Equal(212.00m, match.LastPrice);
        Assert.Equal(new DateOnly(2026, 8, 4), match.LastPriceDate);
    }

    [Fact]
    public async Task Stock_MergesLocalAndProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var localId = await seedDb.SeedInstrumentAsync("CDR.WA", "CD Projekt", PriceSource.Yahoo, "PLN", cancellationToken);
        Factory.InstrumentSearch.WithResults(
            new InstrumentCandidate("CDR.WA", "CD Projekt", AssetClass.Stock, "GPW", "PLN"),
            new InstrumentCandidate("CDRL.WA", "CD Projekt Lab", AssetClass.Stock, "GPW", "PLN"));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(SearchInstrumentsUri("cdr", assetClass: AssetClass.Stock), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!;
        Assert.False(body.ProviderUnavailable);
        Assert.Equal(["CDR.WA", "CDRL.WA"], body.Results.Select(r => r.Ticker));
        Assert.Equal(localId, body.Results[0].Id);
        Assert.Null(body.Results[1].Id);
        Assert.Equal("GPW", body.Results[1].Exchange);
        Assert.Equal("PLN", body.Results[1].QuoteCurrency);
    }

    [Fact]
    public async Task ProviderDown_ReturnsLocalWithFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var localId = await seedDb.SeedInstrumentAsync("VWCE.DE", "Vanguard FTSE All-World", PriceSource.Yahoo, "EUR", cancellationToken, AssetClass.Etf);
        Factory.InstrumentSearch.WithUnavailable();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(SearchInstrumentsUri("vwce", assetClass: AssetClass.Etf), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!;
        Assert.True(body.ProviderUnavailable);
        Assert.Equal(localId, Assert.Single(body.Results).Id);
    }

    [Fact]
    public async Task NonSecurityOrShortQuery_SkipsProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var crypto = await client.GetAsync(SearchInstrumentsUri("bitcoin", assetClass: AssetClass.Crypto), cancellationToken);
        var shortQuery = await client.GetAsync(SearchInstrumentsUri("c", assetClass: AssetClass.Stock), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, crypto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, shortQuery.StatusCode);
        Assert.Empty(Factory.InstrumentSearch.Calls);
    }

    [Fact]
    public async Task Search_ByNamePrefix_ReturnsMatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("CDR.PL", "CD Projekt", PriceSource.Yahoo, "PLN", cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(SearchInstrumentsUri("CD Proj"), cancellationToken);

        var results = (await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!.Results;
        Assert.Equal(instrumentId, Assert.Single(results).Id);
    }

    [Fact]
    public async Task Search_InstrumentWithNoQuoteYet_ReturnsNullLastPrice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("NEW.US", "Brand New Co.", PriceSource.Yahoo, "USD", cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(SearchInstrumentsUri("NEW"), cancellationToken);

        var results = (await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!.Results;
        var match = Assert.Single(results);
        Assert.Equal(instrumentId, match.Id);
        Assert.Null(match.LastPrice);
        Assert.Null(match.LastPriceDate);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(SearchInstrumentsUri("ZZZNOPE"), cancellationToken);

        var results = (await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!.Results;
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_WithoutQuery_ReturnsEmptyRatherThanTheWholeDictionary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(SearchInstrumentsBaseUri, cancellationToken);

        var results = (await response.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!.Results;
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(SearchInstrumentsUri("AAPL"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_AsTwoDifferentUsers_ReturnsIdenticalResults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);

        using var userA = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var userB = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var responseA = await userA.GetAsync(SearchInstrumentsUri("AAPL"), cancellationToken);
        var responseB = await userB.GetAsync(SearchInstrumentsUri("AAPL"), cancellationToken);

        var resultsA = (await responseA.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!.Results;
        var resultsB = (await responseB.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken))!.Results;

        Assert.Equal(resultsA, resultsB);
        Assert.Single(resultsA);
    }
}
