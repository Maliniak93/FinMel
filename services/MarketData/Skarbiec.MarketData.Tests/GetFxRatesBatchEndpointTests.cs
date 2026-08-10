using System.Net;
using System.Net.Http.Json;
using Skarbiec.MarketData.Features.GetFxRatesBatch;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>HTTP-level behavior of Features/GetFxRatesBatch (T2.11) — same shape as GetLatestPricesBatch, keyed by pair.</summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetFxRatesBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private const string LatestBatchUri = "/api/marketdata/fx/latest-batch";

    [Fact]
    public async Task Post_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            LatestBatchUri,
            new { Pairs = new[] { "USDPLN" }, AsOfDate = new DateOnly(2026, 8, 10) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ReturnsLatestRateAtOrBeforeAsOfDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 1), 3.9m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 7), 4.0m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 20), 9.9m, cancellationToken);

        using var client = Factory.CreateSystemAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            LatestBatchUri,
            new { Pairs = new[] { "USDPLN" }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<FxRatesBatchResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rate = Assert.Single(body!.Rates);
        Assert.Equal("USDPLN", rate.Pair);
        Assert.Equal(new DateOnly(2026, 8, 7), rate.Date);
        Assert.Equal(4.0m, rate.Rate);
    }

    [Fact]
    public async Task Post_PairWithNoRateAtAll_IsAbsentFromResponse()
    {
        using var client = Factory.CreateSystemAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            LatestBatchUri,
            new { Pairs = new[] { "EURPLN" }, AsOfDate = new DateOnly(2026, 8, 10) },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<FxRatesBatchResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body!.Rates);
    }
}
