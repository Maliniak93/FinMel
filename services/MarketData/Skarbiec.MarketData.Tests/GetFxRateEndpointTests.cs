using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// An anonymous /internal endpoint, so every fact calls it with no token, as Portfolio does.
public sealed class GetFxRateEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Returns_LatestRateOnOrBeforeDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedFxRateAsync("EURPLN", new DateOnly(2026, 3, 2), 4.30m, cancellationToken);
        await seedDb.SeedFxRateAsync("EURPLN", new DateOnly(2026, 3, 5), 4.35m, cancellationToken);
        // Another pair on the asked date must not be picked up instead.
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 3, 4), 3.99m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", new DateOnly(2026, 3, 4)), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = body.RootElement;
        Assert.Equal("EUR", root.GetProperty("currency").GetString());
        Assert.Equal("2026-03-02", root.GetProperty("date").GetString());
        Assert.Equal(4.30m, root.GetProperty("rate").GetDecimal());
    }

    [Fact]
    public async Task Returns_RateOfTheExactDate_WhenOneExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedFxRateAsync("EURPLN", new DateOnly(2026, 3, 2), 4.30m, cancellationToken);
        await seedDb.SeedFxRateAsync("EURPLN", new DateOnly(2026, 3, 5), 4.35m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", new DateOnly(2026, 3, 5)), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("2026-03-05", body.RootElement.GetProperty("date").GetString());
        Assert.Equal(4.35m, body.RootElement.GetProperty("rate").GetDecimal());
    }

    private static readonly DateOnly OldDate = new(2021, 3, 15);
    private static readonly DateOnly CoverageStart = new(2021, 6, 1);

    private static ScriptedFxRateSource SourceReturning(PriceFetchResult<FxRateQuote> result) => new(historyResult: result);

    private static PriceFetchResult<FxRateQuote> TwoRates() => PriceFetchResult<FxRateQuote>.Success(
    [
        new FxRateQuote("EURPLN", new DateOnly(2021, 3, 10), 4.50m),
        new FxRateQuote("EURPLN", new DateOnly(2021, 3, 12), 4.55m),
    ]);

    [Fact]
    public async Task OlderThanCoverage_ReadsThrough()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = SourceReturning(TwoRates());
        Factory.FxRateSource = source;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedCurrencyCatalogAsync(cancellationToken, CoverageStart);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", OldDate), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("2021-03-12", body.RootElement.GetProperty("date").GetString());
        Assert.Equal(4.55m, body.RootElement.GetProperty("rate").GetDecimal());
        var fetch = Assert.Single(source.HistoryFetches);
        Assert.Equal(new DateOnly(2021, 3, 1), fetch.From);
        Assert.Equal(OldDate, fetch.To);
    }

    [Fact]
    public async Task ReadThrough_DoesNotPersist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateSource = SourceReturning(TwoRates());
        await using var seedDb = CreateDbContext();
        await seedDb.SeedCurrencyCatalogAsync(cancellationToken, CoverageStart);
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 3, 2), 3.90m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", OldDate), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = CreateDbContext();
        var rows = await db.FxRates.AsNoTracking().ToListAsync(cancellationToken);
        var row = Assert.Single(rows);
        Assert.Equal("USDPLN", row.Pair);
    }

    [Fact]
    public async Task InsideCoverage_NoRow_Returns404WithoutSourceCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = SourceReturning(TwoRates());
        Factory.FxRateSource = source;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedCurrencyCatalogAsync(cancellationToken, new DateOnly(2020, 1, 1));

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", OldDate), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, source.HistoryFetchCount);
    }

    [Fact]
    public async Task NbpNoData_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateSource = SourceReturning(PriceFetchResult<FxRateQuote>.NoData());
        await using var seedDb = CreateDbContext();
        await seedDb.SeedCurrencyCatalogAsync(cancellationToken, CoverageStart);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", OldDate), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NotFound.FxRate", await ErrorCodeAsync(response, cancellationToken));
    }

    [Fact]
    public async Task NbpFailure_Returns503()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateSource = SourceReturning(PriceFetchResult<FxRateQuote>.Error("boom"));
        await using var seedDb = CreateDbContext();
        await seedDb.SeedCurrencyCatalogAsync(cancellationToken, CoverageStart);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", OldDate), cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ServiceUnavailable.FxRate", await ErrorCodeAsync(response, cancellationToken));
    }

    [Fact]
    public async Task NbpTimeout_Returns503()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = new NeverAnsweringFxRateSource();
        Factory.FxRateSource = source;
        Factory.FxReadThroughTimeout = TimeSpan.FromMilliseconds(200);
        await using var seedDb = CreateDbContext();
        await seedDb.SeedCurrencyCatalogAsync(cancellationToken, CoverageStart);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("EUR", OldDate), cancellationToken);

        Assert.True(source.Requested.IsCompletedSuccessfully);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ServiceUnavailable.FxRate", await ErrorCodeAsync(response, cancellationToken));
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = JsonSerializer.Deserialize<ProblemDetails>(
            await response.Content.ReadAsStringAsync(cancellationToken), JsonSerializerOptions.Web);
        Assert.NotNull(problem);
        Assert.True(problem.Extensions.TryGetValue("errorCode", out var errorCode));
        return errorCode is JsonElement element ? element.GetString() : errorCode?.ToString();
    }

    // The 404 must be the handler's ProblemDetails, not the empty 404 of an unmapped route.
    [Fact]
    public async Task Returns_NotFound_WhenNoRateOnOrBeforeDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        await seedDb.SeedFxRateAsync("USDPLN", new DateOnly(2026, 3, 10), 3.95m, cancellationToken);

        using var client = Factory.CreateClient();
        var response = await client.GetAsync(InternalFxRateUri("USD", new DateOnly(2026, 3, 4)), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.False(
            string.IsNullOrEmpty(raw),
            "The 404 has no body: the route is not mapped, so this is not the GetFxRate handler's NotFound.");
        var problem = JsonSerializer.Deserialize<ProblemDetails>(raw, JsonSerializerOptions.Web);
        Assert.NotNull(problem);
        Assert.True(
            problem.Extensions.TryGetValue("errorCode", out var errorCode),
            "The 404 carries no errorCode extension, so it did not come from the GetFxRate handler.");
        var code = errorCode is JsonElement element ? element.GetString() : errorCode?.ToString();
        Assert.StartsWith("NotFound.", code);
    }
}
