using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// HTTP-level behavior of Features/SearchInstruments (T2.8 AC): matching, the last-price join, and
/// that the endpoint is authenticated but never tenancy-filtered (MarketData has no per-user data,
/// see ArchitectureTests' remark). The &lt;100 ms performance AC is verified separately, against the
/// handler directly, in <see cref="SearchInstrumentsPerformanceTests"/> — a full HTTP round trip
/// through Kestrel/serialization would be measuring more than the query itself.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SearchInstrumentsEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Search_ByTickerPrefix_ReturnsMatchWithLastPriceAndDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 3), 210.50m, cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 4), 212.00m, cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(SearchInstrumentsUri("AAPL"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);
        var match = Assert.Single(results!);
        Assert.Equal(instrumentId, match.Id);
        Assert.Equal("AAPL.US", match.Ticker);
        Assert.Equal(212.00m, match.LastPrice);
        Assert.Equal(new DateOnly(2026, 8, 4), match.LastPriceDate);
    }

    [Fact]
    public async Task Search_ByNamePrefix_ReturnsMatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("CDR.PL", "CD Projekt", PriceSource.Stooq, "PLN", cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(SearchInstrumentsUri("CD Proj"), cancellationToken);

        var results = await response.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);
        Assert.Equal(instrumentId, Assert.Single(results!).Id);
    }

    [Fact]
    public async Task Search_InstrumentWithNoQuoteYet_ReturnsNullLastPrice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("NEW.US", "Brand New Co.", PriceSource.Stooq, "USD", cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(SearchInstrumentsUri("NEW"), cancellationToken);

        var results = await response.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);
        var match = Assert.Single(results!);
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

        var results = await response.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);
        Assert.Empty(results!);
    }

    [Fact]
    public async Task Search_WithoutQuery_ReturnsEmptyRatherThanTheWholeDictionary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(SearchInstrumentsBaseUri, cancellationToken);

        var results = await response.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);
        Assert.Empty(results!);
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
        await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", cancellationToken);

        using var userA = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var userB = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var responseA = await userA.GetAsync(SearchInstrumentsUri("AAPL"), cancellationToken);
        var responseB = await userB.GetAsync(SearchInstrumentsUri("AAPL"), cancellationToken);

        var resultsA = await responseA.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);
        var resultsB = await responseB.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);

        Assert.Equal(resultsA, resultsB);
        Assert.Single(resultsA!);
    }
}
