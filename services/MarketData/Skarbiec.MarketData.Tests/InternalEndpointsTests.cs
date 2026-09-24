using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// The service-only endpoints left the public <c>/api/marketdata</c> space for <c>/internal</c>
/// (ADR-027): the old public routes are gone, and nothing under <c>/internal</c> leaks into the
/// OpenAPI document the SPA's client is generated from. Every fact calls MarketData directly and
/// asserts on the raw response.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class InternalEndpointsTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task OldPublicBatchPaths_AreNotMapped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        // An instrument with a quote and an FX rate, so a still-mapped old route would answer 200.
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", cancellationToken);
        await seedDb.SeedQuoteAsync(instrumentId, new DateOnly(2026, 8, 5), 160m, cancellationToken);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 8, 7), 4.0m, cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var pricesBatch = await client.PostAsJsonAsync(
            "/api/marketdata/prices/latest-batch",
            new { InstrumentIds = new[] { instrumentId }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);
        var fxBatch = await client.PostAsJsonAsync(
            "/api/marketdata/fx/latest-batch",
            new { Pairs = new[] { "USDPLN" }, AsOfDate = new DateOnly(2026, 8, 10) },
            cancellationToken);

        HttpStatusCode[] unmapped = [HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed];
        Assert.Multiple(
            () => Assert.Contains(pricesBatch.StatusCode, unmapped),
            () => Assert.Contains(fxBatch.StatusCode, unmapped));
    }

    [Fact]
    public async Task OpenApiDocument_ContainsNoInternalPaths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(OpenApiDocumentUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        // Guards against a vacuous pass on an empty document: the public endpoints are still there,
        // including the instrument lookup the SPA's generated client reads.
        Assert.Contains(SearchInstrumentsBaseUri, paths);
        Assert.Multiple(
            () => Assert.Contains("/api/marketdata/instruments/{id}", paths),
            () => Assert.DoesNotContain(paths, p => p.StartsWith("/internal", StringComparison.OrdinalIgnoreCase)),
            () => Assert.DoesNotContain("/api/marketdata/prices/latest-batch", paths),
            () => Assert.DoesNotContain("/api/marketdata/fx/latest-batch", paths));
    }
}
