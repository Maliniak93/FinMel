using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// HTTP-level behavior of Features/GetFxRate (transactions-pln-value-and-fee-removal, ADR-026):
/// Portfolio's request-path lookup of the <c>{currency}PLN</c> rate a transaction is frozen at. A
/// service-only <c>/internal</c> endpoint (ADR-027): anonymous, so every fact calls it with no
/// token, exactly as Portfolio does. Facts call the endpoint directly and assert on the raw
/// response; the wire shape <c>{ currency, date, rate }</c> is read from the JSON itself.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
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

    /// <summary>
    /// Only a rate <em>after</em> the date exists. The 404 must be the handler's own ProblemDetails
    /// (ADR-017 <c>NotFound.*</c> error code), not the empty 404 an unmapped route answers with.
    /// </summary>
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
