using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetLatestPricesBatch;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// An anonymous /internal endpoint, so every fact calls it with no token, as Reporting does.
public sealed class GetLatestPricesBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Post_WithoutToken_ReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 5), 160m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalLatestPricesBatchUri,
            new { InstrumentIds = new[] { instrumentId }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LatestPricesBatchResponse>(cancellationToken);
        var quote = Assert.Single(body!.Quotes);
        Assert.Equal(instrumentId, quote.InstrumentId);
        Assert.Equal(160m, quote.Close);
    }

    [Fact]
    public async Task Post_ReturnsLatestQuoteAtOrBeforeAsOfDate_NotTheNewestOneEverSeen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 1), 150m, cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 5), 160m, cancellationToken);
        // A quote after AsOfDate must never be picked, even though it's the newest overall.
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 20), 999m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalLatestPricesBatchUri,
            new { InstrumentIds = new[] { instrumentId }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LatestPricesBatchResponse>(cancellationToken);
        var quote = Assert.Single(body!.Quotes);
        Assert.Equal(instrumentId, quote.InstrumentId);
        Assert.Equal("USD", quote.QuoteCurrency);
        Assert.Equal(new DateOnly(2026, 8, 5), quote.Date);
        Assert.Equal(160m, quote.Close);
    }

    [Fact]
    public async Task Metal_IsServedPerGram_StockUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var goldId = await seedDb.SeedInstrumentAsync("GC=F", "Gold (1 g)", PriceSource.Yahoo, "USD", cancellationToken, AssetClass.PreciousMetal);
        var stockId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        var friday = new DateOnly(2026, 1, 9);
        await seedDb.SeedQuoteAsync(goldId, friday, 9331.04304m, cancellationToken);
        await seedDb.SeedQuoteAsync(stockId, friday, 160m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalLatestPricesBatchUri,
            new { InstrumentIds = new[] { goldId, stockId }, AsOfDate = new DateOnly(2026, 1, 11) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LatestPricesBatchResponse>(cancellationToken);
        var gold = Assert.Single(body!.Quotes, q => q.InstrumentId == goldId);
        var stock = Assert.Single(body.Quotes, q => q.InstrumentId == stockId);
        Assert.Equal(friday, gold.Date);
        Assert.Equal(300m, gold.Close);
        Assert.Equal(160m, stock.Close);
    }

    [Fact]
    public async Task Post_InstrumentWithNoQuoteAtAll_IsAbsentFromResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("MSFT.US", "Microsoft Corp.", PriceSource.Yahoo, "USD", cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalLatestPricesBatchUri,
            new { InstrumentIds = new[] { instrumentId }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LatestPricesBatchResponse>(cancellationToken);
        Assert.Empty(body!.Quotes);
    }
}
