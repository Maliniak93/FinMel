using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Starter dictionary + currency catalog seed (spec-04 AC1-2, T2.1's original AC: "seed idempotent,
/// re-running doesn't duplicate"). Since design decision 2 deletes the seeder's four bootstrap FX
/// rows outright, the seeder now writes no <see cref="FxRate"/> row at all — <see cref="Sources.FxSyncJob"/>'s
/// own 12-month backfill (spec-04 AC4) is what gives a fresh catalog its first history.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
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
        // The catalog is deliberately wider than the user-facing SupportedCurrencies set (design
        // decision 1) — GBP/CHF back FxSyncJob's coverage even though no user can pick them.
        Assert.Contains("GBP", codes);
        Assert.Contains("CHF", codes);
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
