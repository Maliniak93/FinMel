using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

public sealed class GetBondSeriesEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Get_ExistingSeries_ReturnsEveryPeriodRateOrderedByIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedBondCatalogFromFixtureAsync(cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(BondSeriesUri("ROR0623"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        Assert.Equal("ROR0623", document.RootElement.GetProperty("code").GetString());
        var rates = document.RootElement.GetProperty("periodRates").EnumerateArray().ToArray();
        Assert.Equal(12, rates.Length);
        var indexes = rates.Select(r => r.GetProperty("periodIndex").GetInt32()).ToArray();
        Assert.Equal(indexes.Order(), indexes);
        Assert.Equal(5.25m, rates[0].GetProperty("ratePercent").GetDecimal());
        Assert.Equal(6.75m, rates[^1].GetProperty("ratePercent").GetDecimal());
    }

    [Fact]
    public async Task Get_UnknownCode_ReturnsNotFoundBondSeries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(BondSeriesUri("ROR9999"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(raw, JsonSerializerOptions.Web);
        Assert.NotNull(problem);
        Assert.True(problem.Extensions.TryGetValue("errorCode", out var errorCode));
        Assert.Equal("NotFound.BondSeries", errorCode is JsonElement element ? element.GetString() : errorCode?.ToString());
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(BondSeriesUri("ROR0623"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
