using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class MarketDataSeederTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task SeedAsync_SeedsEveryCurrencyInSupportedCurrencies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateDbContext();

        await MarketDataSeeder.SeedAsync(context, cancellationToken);

        var codes = await context.Currencies.Select(c => c.Code).ToListAsync(cancellationToken);

        Assert.All(SupportedCurrencies.All, code => Assert.Contains(code, codes));
        // The catalog is wider than the user-facing SupportedCurrencies set.
        Assert.Contains("GBP", codes);
        Assert.Contains("CHF", codes);
    }

    [Fact]
    public async Task Seeds_MetalInstruments_WithFixedIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateDbContext();

        await MarketDataSeeder.SeedAsync(context, cancellationToken);
        await MarketDataSeeder.SeedAsync(context, cancellationToken);

        var instruments = await context.Instruments.ToListAsync(cancellationToken);
        var gold = Assert.Single(instruments, i => i.Ticker == "XAU");
        var silver = Assert.Single(instruments, i => i.Ticker == "XAG");
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Gold), gold.Id);
        Assert.Equal(MetalInstruments.InstrumentIdFor(Metal.Silver), silver.Id);
        Assert.All([gold, silver], i =>
        {
            Assert.Equal(PriceSource.GoldApi, i.Source);
            Assert.Equal("USD", i.QuoteCurrency);
            Assert.Equal(AssetClass.PreciousMetal, i.AssetClass);
        });
        Assert.DoesNotContain(instruments, i => i.Source.ToString() == "Nbp");
    }

    [Fact]
    public async Task Seeds_YahooStockAndEtf_NotTheOldStooqTickers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateDbContext();

        await MarketDataSeeder.SeedAsync(context, cancellationToken);

        var instruments = await context.Instruments.ToListAsync(cancellationToken);
        var stock = Assert.Single(instruments, i => i.Ticker == "CDR.WA");
        Assert.Equal(AssetClass.Stock, stock.AssetClass);
        Assert.Equal("PLN", stock.QuoteCurrency);
        Assert.Equal(PriceSource.Yahoo, stock.Source);
        var etf = Assert.Single(instruments, i => i.Ticker == "VWCE.DE");
        Assert.Equal(AssetClass.Etf, etf.AssetClass);
        Assert.Equal("EUR", etf.QuoteCurrency);
        Assert.Equal(PriceSource.Yahoo, etf.Source);
        Assert.DoesNotContain(instruments, i => i.Ticker is "AAPL.US" or "CDR.PL");
    }

    [Fact]
    public async Task SeedAsync_WritesNoBootstrapFxRates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateDbContext();

        await MarketDataSeeder.SeedAsync(context, cancellationToken);

        Assert.Equal(0, await context.FxRates.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task SeedAsync_CalledTwice_DoesNotDuplicateInstrumentsOrCurrencies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = CreateDbContext();

        await MarketDataSeeder.SeedAsync(context, cancellationToken);
        var instrumentCountAfterFirstSeed = await context.Instruments.CountAsync(cancellationToken);
        var currencyCountAfterFirstSeed = await context.Currencies.CountAsync(cancellationToken);

        await MarketDataSeeder.SeedAsync(context, cancellationToken);
        var instrumentCountAfterSecondSeed = await context.Instruments.CountAsync(cancellationToken);
        var currencyCountAfterSecondSeed = await context.Currencies.CountAsync(cancellationToken);

        Assert.True(instrumentCountAfterFirstSeed > 0);
        Assert.True(currencyCountAfterFirstSeed > 0);
        Assert.Equal(instrumentCountAfterFirstSeed, instrumentCountAfterSecondSeed);
        Assert.Equal(currencyCountAfterFirstSeed, currencyCountAfterSecondSeed);
    }
}
