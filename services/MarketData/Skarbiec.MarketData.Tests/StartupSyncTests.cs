using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.TriggerSync;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class StartupSyncTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    [Fact]
    public async Task StaleKinds_RunInOrder_WithStartupSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = await SyncHost.StartAsync(_containers, new NoOpFxRateSource(), FakeMfBondSource.FromFixture(), cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            await SyncHost.WaitUntilAsync(
                async () => await db.SyncRuns.AsNoTracking().CountAsync(r => r.Status == SyncRunStatus.Completed, cancellationToken) == 3,
                cancellationToken);

            var runs = await db.SyncRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync(cancellationToken);
            Assert.Equal([SyncRunKind.Fx, SyncRunKind.Prices, SyncRunKind.BondCatalog], runs.Select(r => r.Kind));
            Assert.All(runs, r => Assert.Equal(SyncRunSource.Startup, r.Source));
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task FxGapStoredBeforePricesEventPublished()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastDay = today.AddDays(-5);
        var gapDay = today.AddDays(-3);
        string[] codes = ["EUR", "USD", "GBP", "CHF"];

        Guid instrumentId;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedCurrencyCatalogAsync(cancellationToken);
            foreach (var code in codes)
            {
                await seedDb.SeedFxRateAsync($"{code}PLN", lastDay, 4m, cancellationToken);
            }

            instrumentId = await seedDb.SeedInstrumentAsync("AAPL.US", "Apple", PriceSource.Yahoo, "USD", cancellationToken);
            seedDb.InstrumentUsages.Add(new InstrumentUsage { InstrumentId = instrumentId, AssetCount = 1, FirstUsedAt = DateTimeOffset.UtcNow });
            await seedDb.SaveChangesAsync(cancellationToken);
            await seedDb.SeedQuoteAsync(instrumentId, lastDay, 100m, cancellationToken);
        }

        var fxSource = new ScriptedFxRateSource(
            PriceFetchResult<FxRateQuote>.Success(codes.Select(c => new FxRateQuote($"{c}PLN", today, 4m)).ToList()),
            historyResultsByCode: codes.ToDictionary(
                c => c,
                c => PriceFetchResult<FxRateQuote>.Success([new FxRateQuote($"{c}PLN", gapDay, 4.1m)])));

        await using var probeDb = CreateDbContext();
        var priceSource = new ProbingPriceSource(
            async () => await probeDb.FxRates.AsNoTracking().CountAsync(r => r.Date == gapDay, cancellationToken) == codes.Length,
            _ => [new InstrumentQuote(instrumentId, gapDay, 101m)]);

        var host = await SyncHost.StartAsync(
            _containers, fxSource, FakeMfBondSource.FromFixture(), cancellationToken, priceSources: [priceSource]);
        try
        {
            var fxGapAlreadyStored = await priceSource.ProbeResult.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            Assert.True(fxGapAlreadyStored, "the prices run fetched before the FX gap was stored.");
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task FreshKind_IsSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedSyncRunAsync(SyncRunKind.Prices, SyncRunStatus.Completed, DateTimeOffset.UtcNow.AddSeconds(-1), cancellationToken);
        }

        var host = await SyncHost.StartAsync(_containers, new NoOpFxRateSource(), FakeMfBondSource.FromFixture(), cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            await SyncHost.WaitUntilAsync(
                () => db.SyncRuns.AsNoTracking().AnyAsync(
                    r => r.Kind == SyncRunKind.BondCatalog && r.Status == SyncRunStatus.Completed, cancellationToken),
                cancellationToken);

            Assert.Equal(1, await db.SyncRuns.CountAsync(r => r.Kind == SyncRunKind.Prices, cancellationToken));
            Assert.True(await db.SyncRuns.AnyAsync(r => r.Kind == SyncRunKind.Fx && r.Source == SyncRunSource.Startup, cancellationToken));
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task FailedOrPartialRun_RetriedOnlyAfterMinInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedSyncRunAsync(SyncRunKind.Fx, SyncRunStatus.Partial, DateTimeOffset.UtcNow.AddMinutes(-2), cancellationToken);
            await seedDb.SeedSyncRunAsync(SyncRunKind.Prices, SyncRunStatus.Failed, DateTimeOffset.UtcNow.AddMinutes(-30), cancellationToken);
            await seedDb.SeedSyncRunAsync(SyncRunKind.BondCatalog, SyncRunStatus.Failed, DateTimeOffset.UtcNow.AddMinutes(-2), cancellationToken);
        }

        var host = await SyncHost.StartAsync(_containers, new NoOpFxRateSource(), FakeMfBondSource.FromFixture(), cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            await SyncHost.WaitUntilAsync(
                () => db.SyncRuns.AsNoTracking().AnyAsync(
                    r => r.Kind == SyncRunKind.Prices && r.Source == SyncRunSource.Startup && r.Status == SyncRunStatus.Completed,
                    cancellationToken),
                cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            Assert.Equal(2, await db.SyncRuns.CountAsync(r => r.Kind == SyncRunKind.Prices, cancellationToken));
            Assert.Equal(1, await db.SyncRuns.CountAsync(r => r.Kind == SyncRunKind.Fx, cancellationToken));
            Assert.Equal(1, await db.SyncRuns.CountAsync(r => r.Kind == SyncRunKind.BondCatalog, cancellationToken));
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task SlowProvider_DoesNotBlockStartup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedCurrencyCatalogAsync(cancellationToken);
        }

        var fxSource = new NeverAnsweringFxRateSource();
        var host = await SyncHost.StartAsync(_containers, fxSource, FakeMfBondSource.FromFixture(), cancellationToken).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        try
        {
            await fxSource.Requested.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            await using var db = CreateDbContext();
            var fxRun = await db.SyncRuns.AsNoTracking().SingleAsync(r => r.Kind == SyncRunKind.Fx, cancellationToken);
            Assert.Equal(SyncRunStatus.Running, fxRun.Status);

            await using var scope = host.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<TriggerSyncHandler>().HandleAsync(cancellationToken);
            Assert.True(result.IsFailure);
            Assert.Equal("Conflict.SyncAlreadyRunning", result.Error.Code);
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task OrphanedRunningRows_AreMarkedFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid orphanId;
        await using (var seedDb = CreateDbContext())
        {
            var orphan = await seedDb.SeedSyncRunAsync(SyncRunKind.Prices, SyncRunStatus.Running, DateTimeOffset.UtcNow.AddHours(-1), cancellationToken);
            orphanId = orphan.Id;
        }

        var host = await SyncHost.StartAsync(_containers, new NoOpFxRateSource(), FakeMfBondSource.FromFixture(), cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            await SyncHost.WaitUntilAsync(
                async () => (await db.SyncRuns.AsNoTracking().SingleAsync(r => r.Id == orphanId, cancellationToken)).Status != SyncRunStatus.Running,
                cancellationToken);

            var orphanRun = await db.SyncRuns.AsNoTracking().SingleAsync(r => r.Id == orphanId, cancellationToken);
            Assert.Equal(SyncRunStatus.Failed, orphanRun.Status);
            Assert.NotNull(orphanRun.FinishedAt);
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task Disabled_RunsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = await SyncHost.StartAsync(
            _containers,
            new NoOpFxRateSource(),
            FakeMfBondSource.FromFixture(),
            cancellationToken,
            new Dictionary<string, string?> { ["StartupSync:Enabled"] = "false" });
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var db = CreateDbContext();
            Assert.False(await db.SyncRuns.AnyAsync(cancellationToken));
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task DisabledBackgroundJobs_RunsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = await SyncHost.StartAsync(
            _containers,
            new NoOpFxRateSource(),
            FakeMfBondSource.FromFixture(),
            cancellationToken,
            new Dictionary<string, string?> { ["Testing:DisableBackgroundJobs"] = "true" });
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var db = CreateDbContext();
            Assert.False(await db.SyncRuns.AnyAsync(cancellationToken));
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }
}
