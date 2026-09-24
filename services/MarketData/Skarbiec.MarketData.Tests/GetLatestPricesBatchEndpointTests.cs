using System.Net;
using System.Net.Http.Json;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetLatestPricesBatch;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// HTTP-level behavior of Features/GetLatestPricesBatch (T2.11) — Reporting's DailyPricesSynced
/// consumer calls it. A service-only <c>/internal</c> endpoint (ADR-027): anonymous, so every fact
/// calls it with no token, exactly as Reporting does.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetLatestPricesBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Post_WithoutToken_ReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", cancellationToken);
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
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", cancellationToken);
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
    public async Task Post_InstrumentWithNoQuoteAtAll_IsAbsentFromResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("MSFT.US", "Microsoft Corp.", PriceSource.Stooq, "USD", cancellationToken);

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
