using System.Net;
using System.Net.Http.Json;
using Skarbiec.MarketData.Features.GetFxRatesBatch;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// HTTP-level behavior of Features/GetFxRatesBatch (T2.11) — same shape as GetLatestPricesBatch,
/// keyed by pair. A service-only <c>/internal</c> endpoint (ADR-027): anonymous, so every fact calls
/// it with no token, exactly as Reporting does.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetFxRatesBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Post_WithoutToken_ReturnsOk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 7), 4.0m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalFxRatesBatchUri,
            new { Pairs = new[] { "USDPLN" }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FxRatesBatchResponse>(cancellationToken);
        var rate = Assert.Single(body!.Rates);
        Assert.Equal("USDPLN", rate.Pair);
        Assert.Equal(4.0m, rate.Rate);
    }

    [Fact]
    public async Task Post_ReturnsLatestRateAtOrBeforeAsOfDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 1), 3.9m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 7), 4.0m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 20), 9.9m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalFxRatesBatchUri,
            new { Pairs = new[] { "USDPLN" }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FxRatesBatchResponse>(cancellationToken);
        var rate = Assert.Single(body!.Rates);
        Assert.Equal("USDPLN", rate.Pair);
        Assert.Equal(new DateOnly(2026, 8, 7), rate.Date);
        Assert.Equal(4.0m, rate.Rate);
    }

    [Fact]
    public async Task Post_PairWithNoRateAtAll_IsAbsentFromResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            InternalFxRatesBatchUri,
            new { Pairs = new[] { "EURPLN" }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FxRatesBatchResponse>(cancellationToken);
        Assert.Empty(body!.Rates);
    }
}
