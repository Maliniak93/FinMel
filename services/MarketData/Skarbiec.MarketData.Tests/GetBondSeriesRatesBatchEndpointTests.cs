using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// An anonymous /internal endpoint, so every fact calls it with no token, as Portfolio does.
[Collection(TestingDefaults.CollectionName)]
public sealed class GetBondSeriesRatesBatchEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Post_KnownAndUnknownCode_ReturnsKnownSeriesRatesAndSkipsUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedBondSeriesAsync(
                "EDO1035", TreasuryBondType.Edo, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31), cancellationToken, periodRatesPercent: [5.35m, 4.00m]);
        }

        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(InternalBondSeriesRatesBatchUri, new { codes = new[] { "EDO1035", "EDO9999" } }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var series = Assert.Single(document.RootElement.GetProperty("series").EnumerateArray().ToArray());
        Assert.Equal("EDO1035", series.GetProperty("code").GetString());
        var rates = series.GetProperty("periodRates").EnumerateArray().ToArray();
        Assert.Equal([0, 1], rates.Select(r => r.GetProperty("periodIndex").GetInt32()));
        Assert.Equal([5.35m, 4.00m], rates.Select(r => r.GetProperty("ratePercent").GetDecimal()));
    }
}
