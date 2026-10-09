using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// Every fact calls MarketData directly and asserts on the raw response.
public sealed class InternalEndpointsTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task OldPublicBatchPaths_AreNotMapped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        // An instrument with a quote and an FX rate, so a still-mapped old route would answer 200.
        var instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
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
    public async Task BondSeriesRatesBatch_IsAnonymousAndAbsentFromOpenApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedBondSeriesAsync(
                "EDO1035", TreasuryBondType.Edo, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), cancellationToken, periodRatesPercent: [5.35m]);
        }

        using var client = Factory.CreateClient();
        var batch = await client.PostAsJsonAsync(InternalBondSeriesRatesBatchUri, new { codes = new[] { "EDO1035" } }, cancellationToken);
        var openApi = await client.GetAsync(OpenApiDocumentUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        using var document = await JsonDocument.ParseAsync(await openApi.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain(InternalBondSeriesRatesBatchUri, paths);
    }

    [Fact]
    public async Task InstrumentsBatch_IsAnonymousAndAbsentFromOpenApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid instrumentId;
        await using (var seedDb = CreateDbContext())
        {
            instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple Inc.", PriceSource.Yahoo, "USD", cancellationToken);
        }

        using var client = Factory.CreateClient();
        var batch = await client.PostAsJsonAsync(InternalInstrumentsBatchUri, new { instrumentIds = new[] { instrumentId } }, cancellationToken);
        var openApi = await client.GetAsync(OpenApiDocumentUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        using var document = await JsonDocument.ParseAsync(await openApi.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain(InternalInstrumentsBatchUri, paths);
    }

    [Fact]
    public async Task HistoryBatches_AreAnonymousAndAbsentFromOpenApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var from = new DateOnly(2026, 1, 5);
        var to = new DateOnly(2026, 1, 10);
        using var client = Factory.CreateClient();

        var pricesBatch = await client.PostAsJsonAsync(
            InternalPricesHistoryBatchUri,
            new { InstrumentIds = new[] { Guid.NewGuid() }, From = from, To = to },
            cancellationToken);
        var fxBatch = await client.PostAsJsonAsync(
            InternalFxRatesHistoryBatchUri,
            new { Pairs = new[] { "USDPLN" }, From = from, To = to },
            cancellationToken);
        var openApi = await client.GetAsync(OpenApiDocumentUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, pricesBatch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fxBatch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        using var document = await JsonDocument.ParseAsync(await openApi.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        // Guards against a vacuous pass on an empty document.
        Assert.Contains(SearchInstrumentsBaseUri, paths);
        Assert.Multiple(
            () => Assert.DoesNotContain(InternalPricesHistoryBatchUri, paths),
            () => Assert.DoesNotContain(InternalFxRatesHistoryBatchUri, paths));
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

        // Guards against a vacuous pass on an empty document.
        Assert.Contains(SearchInstrumentsBaseUri, paths);
        Assert.Multiple(
            () => Assert.Contains("/api/marketdata/instruments/{id}", paths),
            () => Assert.DoesNotContain(paths, p => p.StartsWith("/internal", StringComparison.OrdinalIgnoreCase)),
            () => Assert.DoesNotContain("/api/marketdata/prices/latest-batch", paths),
            () => Assert.DoesNotContain("/api/marketdata/fx/latest-batch", paths));
    }
}
