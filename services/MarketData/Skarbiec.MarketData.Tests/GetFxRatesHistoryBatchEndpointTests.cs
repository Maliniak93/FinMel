using System.Net;
using System.Net.Http.Json;
using Skarbiec.MarketData.Features.GetFxRatesHistoryBatch;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// An anonymous /internal endpoint, so every fact calls it with no token, as Reporting does.
public sealed class GetFxRatesHistoryBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Post_ReturnsRatesInRangePlusLastBeforeFrom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 1, 2), 4.00m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 1, 5), 4.05m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 1, 6), 4.06m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 1, 12), 4.12m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            InternalFxRatesHistoryBatchUri,
            new { Pairs = new[] { "USDPLN" }, From = new DateOnly(2026, 1, 5), To = new DateOnly(2026, 1, 10) },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FxRatesHistoryBatchResponse>(cancellationToken);
        var series = Assert.Single(body!.Series);
        Assert.Equal("USDPLN", series.Pair);
        Assert.Equal(
            new[] { new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6) },
            series.Rates.Select(r => r.Date));
        Assert.Equal(new[] { 4.00m, 4.05m, 4.06m }, series.Rates.Select(r => r.Rate));
    }

    [Fact]
    public async Task Post_InvalidRequest_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var from = new DateOnly(2026, 1, 5);
        var to = new DateOnly(2026, 1, 10);
        using var client = Factory.CreateClient();

        var fromAfterTo = await client.PostAsJsonAsync(
            InternalFxRatesHistoryBatchUri,
            new { Pairs = new[] { "USDPLN" }, From = to, To = from },
            cancellationToken);
        var emptyPairs = await client.PostAsJsonAsync(
            InternalFxRatesHistoryBatchUri,
            new { Pairs = Array.Empty<string>(), From = from, To = to },
            cancellationToken);
        var tooManyPairs = await client.PostAsJsonAsync(
            InternalFxRatesHistoryBatchUri,
            new { Pairs = Enumerable.Range(0, 1001).Select(i => $"C{i}PLN").ToArray(), From = from, To = to },
            cancellationToken);

        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.BadRequest, fromAfterTo.StatusCode),
            () => Assert.Equal(HttpStatusCode.BadRequest, emptyPairs.StatusCode),
            () => Assert.Equal(HttpStatusCode.BadRequest, tooManyPairs.StatusCode));
    }
}
