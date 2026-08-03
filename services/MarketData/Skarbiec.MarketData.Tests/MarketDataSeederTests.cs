using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Proves the starter dictionary seed (T2.1 AC: "seed idempotent, re-running doesn't duplicate")
/// is safe to call more than once, e.g. on every service startup.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class MarketDataSeederTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task SeedAsync_CalledTwice_DoesNotDuplicateInstrumentsOrFxRates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var context = CreateDbContext();

        await MarketDataSeeder.SeedAsync(context, cancellationToken);
        var instrumentCountAfterFirstSeed = await context.Instruments.CountAsync(cancellationToken);
        var fxRateCountAfterFirstSeed = await context.FxRates.CountAsync(cancellationToken);

        await MarketDataSeeder.SeedAsync(context, cancellationToken);
        var instrumentCountAfterSecondSeed = await context.Instruments.CountAsync(cancellationToken);
        var fxRateCountAfterSecondSeed = await context.FxRates.CountAsync(cancellationToken);

        Assert.True(instrumentCountAfterFirstSeed > 0);
        Assert.True(fxRateCountAfterFirstSeed > 0);
        Assert.Equal(instrumentCountAfterFirstSeed, instrumentCountAfterSecondSeed);
        Assert.Equal(fxRateCountAfterFirstSeed, fxRateCountAfterSecondSeed);
    }
}
