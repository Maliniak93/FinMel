using System.Net;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListBondSeriesEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly string[] OctoberOffer =
        ["OTS0127", "ROR1027", "DOR1028", "TOS1029", "COI1030", "EDO1036", "ROS1032", "ROD1038"];

    private static async Task<JsonElement[]> ReadItemsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    [Fact]
    public async Task List_OnSaleOnDate_ReturnsTheSeriesOnSaleOrderedByType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedBondCatalogFromFixtureAsync(cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(BondSeriesUri(new DateOnly(2026, 10, 15)), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadItemsAsync(response, cancellationToken);
        Assert.Equal(OctoberOffer, items.Select(i => i.GetProperty("code").GetString()));
        var edo = items.Single(i => i.GetProperty("code").GetString() == "EDO1036");
        Assert.Equal(5.35m, edo.GetProperty("firstPeriodRatePercent").GetDecimal());
    }

    [Fact]
    public async Task List_WithoutDate_ReturnsTheSeriesOnSaleInWarsawToday()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // InvariantGlobalization leaves Windows without ICU, so it needs the registry id.
        var warsaw = TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var iana)
            ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, warsaw).DateTime);
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedBondSeriesAsync("EDO0001", TreasuryBondType.Edo, today.AddDays(-3), today.AddDays(3), cancellationToken);
            await seedDb.SeedBondSeriesAsync("EDO0002", TreasuryBondType.Edo, today.AddDays(-30), today.AddDays(-2), cancellationToken);
            await seedDb.SeedBondSeriesAsync("EDO0003", TreasuryBondType.Edo, today.AddDays(2), today.AddDays(30), cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(BondSeriesUri(), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadItemsAsync(response, cancellationToken);
        Assert.Equal(["EDO0001"], items.Select(i => i.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(BondSeriesUri(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
