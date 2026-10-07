using System.Net;
using System.Net.Http.Json;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetInstrument;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// One handler backs the authorized public route and its anonymous /internal twin.
[Collection(TestingDefaults.CollectionName)]
public sealed class GetInstrumentEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Internal_Get_WithoutToken_ReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 4), 212.00m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalInstrumentUri(instrumentId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InstrumentDetailsResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(instrumentId, body.Id);
        Assert.Equal("AAPL.US", body.Ticker);
        Assert.Equal("USD", body.QuoteCurrency);
        Assert.Equal(212.00m, body.LastPrice);
        Assert.Equal(new DateOnly(2026, 8, 4), body.LastPriceDate);
    }

    [Fact]
    public async Task Get_ExistingInstrument_ReturnsOkWithDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 3), 210.50m, cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 4), 212.00m, cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(InstrumentUri(instrumentId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InstrumentDetailsResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(instrumentId, body.Id);
        Assert.Equal("AAPL.US", body.Ticker);
        Assert.Equal("USD", body.QuoteCurrency);
        Assert.Equal(PriceSource.Yahoo, body.Source);
        Assert.Equal(212.00m, body.LastPrice);
        Assert.Equal(new DateOnly(2026, 8, 4), body.LastPriceDate);
    }

    [Fact]
    public async Task Get_InstrumentWithNoQuoteYet_ReturnsNullLastPrice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("NEW.US", "Brand New Co.", PriceSource.Yahoo, "USD", cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(InstrumentUri(instrumentId), cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<InstrumentDetailsResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Null(body.LastPrice);
        Assert.Null(body.LastPriceDate);
    }

    [Fact]
    public async Task Get_NonExistentInstrument_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(InstrumentUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(InstrumentUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
