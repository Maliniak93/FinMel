using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetMetalPrices;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class GetMetalPricesEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private const string MetalPricesUri = "/api/marketdata/metal-prices";

    [Fact]
    public async Task Get_GoldQuoteAndRate_ReturnsPlnPrices_SilverWithoutQuoteIsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAsync(
            goldDate: new DateOnly(2026, 10, 6), goldPricePerGramUsd: 133.92m,
            rateDate: new DateOnly(2026, 10, 5), usdPlnRate: 3.9m, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(MetalPricesUri, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<MetalPriceResponse>>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        var gold = Assert.Single(items, i => i.Metal == Metal.Gold);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Gold), gold.InstrumentId);
        Assert.Equal(new DateOnly(2026, 10, 6), gold.Date);
        Assert.Equal(133.92m, gold.PricePerGramUsd);
        Assert.Equal(3.9m, gold.UsdPlnRate);
        Assert.Equal(522.29m, gold.PricePerGramPln);
        Assert.Equal(Math.Round(133.92m * 3.9m * 31.1034768m, 2), gold.PricePerTroyOuncePln);
        var silver = Assert.Single(items, i => i.Metal == Metal.Silver);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Silver), silver.InstrumentId);
        Assert.Null(silver.Date);
        Assert.Null(silver.PricePerGramUsd);
        Assert.Null(silver.UsdPlnRate);
        Assert.Null(silver.PricePerGramPln);
        Assert.Null(silver.PricePerTroyOuncePln);
    }

    [Fact]
    public async Task StalePrice_IsFlagged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedAsync(
            goldDate: today.AddDays(-10), goldPricePerGramUsd: 133.92m,
            rateDate: today.AddDays(-10), usdPlnRate: 3.9m, cancellationToken);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(MetalPricesUri, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<MetalPriceResponse>>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Assert.Single(items!, i => i.Metal == Metal.Gold).IsStale);
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(MetalPricesUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task SeedAsync(
        DateOnly goldDate, decimal goldPricePerGramUsd, DateOnly rateDate, decimal usdPlnRate, CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        await MarketDataSeeder.SeedAsync(db, cancellationToken);
        db.PriceQuotes.Add(new PriceQuote
        {
            Id = Guid.NewGuid(),
            InstrumentId = MetalInstruments.InstrumentIdFor(Metal.Gold),
            Date = goldDate,
            Close = goldPricePerGramUsd,
        });
        db.FxRates.Add(new FxRate { Id = Guid.NewGuid(), Pair = "USDPLN", Date = rateDate, Rate = usdPlnRate });
        await db.SaveChangesAsync(cancellationToken);
    }
}
