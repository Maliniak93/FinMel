using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// A real DI-wired scheduler: TriggerSyncEndpointTests run against NoOpSyncTrigger, so firing and the double-click guard need this.
[Collection(TestingDefaults.CollectionName)]
public sealed class SyncTriggerSchedulingTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task TriggerAsync_FiresPriceSyncJob_OutsideItsCronSchedule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("XYZ.WA", "Xyz SA", PriceSource.Stooq, "PLN", cancellationToken);
        await SeedInUseAsync(seedDb, instrumentId, cancellationToken);

        var (host, _) = await BuildHostAsync(
            new GatedPriceSource(
                PriceSource.Stooq,
                CompletedGate(),
                PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrumentId, Today, 42.50m)])));
        try
        {
            var syncTrigger = host.Services.GetRequiredService<ISyncTrigger>();

            var outcome = await syncTrigger.TriggerAsync(cancellationToken);

            Assert.Equal(SyncTriggerOutcome.Started, outcome);

            await using var db = CreateDbContext();
            var run = await WaitForFinishedRunAsync(db, cancellationToken);
            Assert.Equal(SyncRunStatus.Completed, run.Status);
            Assert.Equal(1, run.SyncedCount);

            var quote = await db.PriceQuotes.AsNoTracking().SingleAsync(
                q => q.InstrumentId == instrumentId && q.Date == Today, cancellationToken);
            Assert.Equal(42.50m, quote.Close);
        }
        finally
        {
            await CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task TriggerAsync_WhileARunIsStillInFlight_ReturnsAlreadyRunning_AndOnlyOneRunHappens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("SLOW.WA", "Slow SA", PriceSource.Stooq, "PLN", cancellationToken);
        await SeedInUseAsync(seedDb, instrumentId, cancellationToken);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (host, _) = await BuildHostAsync(
            new GatedPriceSource(
                PriceSource.Stooq, gate, PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrumentId, Today, 10m)])));
        try
        {
            var syncTrigger = host.Services.GetRequiredService<ISyncTrigger>();

            var firstOutcome = await syncTrigger.TriggerAsync(cancellationToken);
            Assert.Equal(SyncTriggerOutcome.Started, firstOutcome);

            // Wait until the run has really started, so the second click meets an in-flight run, not a queued trigger.
            await using var db = CreateDbContext();
            await WaitUntilAsync(() => db.SyncRuns.AsNoTracking().AnyAsync(cancellationToken), cancellationToken);

            var secondOutcome = await syncTrigger.TriggerAsync(cancellationToken);

            Assert.Equal(SyncTriggerOutcome.AlreadyRunning, secondOutcome);

            gate.SetResult();

            var run = await WaitForFinishedRunAsync(db, cancellationToken);
            Assert.Equal(1, run.SyncedCount);
            Assert.Equal(1, await db.SyncRuns.CountAsync(cancellationToken));
        }
        finally
        {
            gate.TrySetResult();
            await CleanUpAsync(host, cancellationToken);
        }
    }

    // PriceSyncJob syncs only instruments in use.
    private static async Task SeedInUseAsync(MarketDataDbContext db, Guid instrumentId, CancellationToken cancellationToken)
    {
        db.InstrumentUsages.Add(new InstrumentUsage { InstrumentId = instrumentId, AssetCount = 1, FirstUsedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TaskCompletionSource CompletedGate()
    {
        var gate = new TaskCompletionSource();
        gate.SetResult();
        return gate;
    }

    private async Task<(IHost Host, ISchedulerFactory SchedulerFactory)> BuildHostAsync(IPriceSource priceSource)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:marketdata-db"] = _containers.PostgresConnectionString;
        builder.Configuration["ConnectionStrings:rabbitmq"] = _containers.RabbitMqConnectionString;
        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(_containers.PostgresConnectionString));
        builder.Services.AddSingleton<IFxRateSource>(new NoOpFxRateSource());
        builder.Services.AddSingleton<IPriceSource>(priceSource);
        builder.AddRabbitMqMessaging<HostApplicationBuilder, MarketDataDbContext>();
        builder.AddPriceSyncJob();

        // Deliberately not disposed: Quartz's LogProvider caches this host's ILoggerFactory in a process-wide static.
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        return (host, host.Services.GetRequiredService<ISchedulerFactory>());
    }

    private static async Task CleanUpAsync(IHost host, CancellationToken cancellationToken)
    {
        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        // Not DeleteJob: it throws once a fired one-shot manual trigger lingers in Complete state, and each test owns its host.
        await scheduler.Clear(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    private static async Task<SyncRun> WaitForFinishedRunAsync(MarketDataDbContext db, CancellationToken cancellationToken)
    {
        SyncRun? run = null;
        await WaitUntilAsync(async () =>
        {
            run = await db.SyncRuns.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            return run?.FinishedAt is not null;
        }, cancellationToken);

        return run!;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        Assert.Fail("Condition was not met within the 15s deadline.");
    }
}
