using System.Net;
using System.Net.Http.Json;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetLatestPricesBatch;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>HTTP-level behavior of Features/GetLatestPricesBatch (T2.11) — Reporting's DailyPricesSynced consumer calls this internally.</summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetLatestPricesBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private const string LatestBatchUri = "/api/marketdata/prices/latest-batch";

    [Fact]
    public async Task Post_WithOrdinaryUserToken_ReturnsForbidden()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            LatestBatchUri,
            new { InstrumentIds = new[] { Guid.NewGuid() }, AsOfDate = new DateOnly(2026, 8, 10) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        using var client = Factory.CreateSystemAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            LatestBatchUri,
            new { InstrumentIds = new[] { instrumentId }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<LatestPricesBatchResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

        using var client = Factory.CreateSystemAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            LatestBatchUri,
            new { InstrumentIds = new[] { instrumentId }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<LatestPricesBatchResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body!.Quotes);
    }
}
