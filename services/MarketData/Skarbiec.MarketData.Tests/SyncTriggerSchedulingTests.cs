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

/// <summary>
/// T2.14's manual trigger against a real, DI-wired Quartz scheduler (<see cref="PriceSyncJobExtensions.AddPriceSyncJob"/>)
/// — <see cref="TriggerSyncEndpointTests"/> only exercises the HTTP surface against
/// <see cref="NoOpSyncTrigger"/> (no live scheduler under <c>Testing:DisableBackgroundJobs</c>), so
/// the actual firing mechanism and the double-click guard need a real one, same reasoning as
/// <see cref="PriceSyncSchedulingTests"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SyncTriggerSchedulingTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // Explicit field: the primary constructor parameter is also passed to the base constructor
    // above, so referencing it directly elsewhere in this class would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task TriggerAsync_FiresPriceSyncJob_OutsideItsCronSchedule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var instrumentId = await seedDb.SeedInstrumentAsync("XYZ.WA", "Xyz SA", PriceSource.Stooq, "PLN", cancellationToken);

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

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (host, _) = await BuildHostAsync(
            new GatedPriceSource(
                PriceSource.Stooq, gate, PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrumentId, Today, 10m)])));
        try
        {
            var syncTrigger = host.Services.GetRequiredService<ISyncTrigger>();

            var firstOutcome = await syncTrigger.TriggerAsync(cancellationToken);
            Assert.Equal(SyncTriggerOutcome.Started, firstOutcome);

            // Wait until PriceSyncJob.RunAsync has genuinely started (its SyncRun row exists) before
            // the "second click" — proves the guard sees an in-flight run, not just a queued trigger.
            await using var db = CreateDbContext();
            await WaitUntilAsync(() => db.SyncRuns.AsNoTracking().AnyAsync(cancellationToken), cancellationToken);

            var secondOutcome = await syncTrigger.TriggerAsync(cancellationToken);

            Assert.Equal(SyncTriggerOutcome.AlreadyRunning, secondOutcome);

            gate.SetResult(); // let the first (only) run finish.

            var run = await WaitForFinishedRunAsync(db, cancellationToken);
            Assert.Equal(1, run.SyncedCount);
            Assert.Equal(1, await db.SyncRuns.CountAsync(cancellationToken)); // never ran twice.
        }
        finally
        {
            gate.TrySetResult();
            await CleanUpAsync(host, cancellationToken);
        }
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

        // Deliberately not disposed — see PriceSyncSchedulingTests' identical remark: disposing here
        // would poison Quartz.Logging.LogProvider's process-wide cached ILoggerFactory for every later
        // scheduler in this test process.
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        return (host, host.Services.GetRequiredService<ISchedulerFactory>());
    }

    private static async Task CleanUpAsync(IHost host, CancellationToken cancellationToken)
    {
        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        // Plain DeleteJob(PriceSyncJob.Key) throws once a fired one-shot manual trigger is involved
        // (Quartz keeps it around in "Complete" state instead of auto-removing it, and DeleteJob's own
        // unschedule-all-triggers step chokes on that combination) — Clear() wipes all scheduling data
        // for this store instead, which is fine here since each test builds its own throwaway host and
        // this collection serializes test classes against the shared containers (no cross-test race).
        await scheduler.Clear(cancellationToken); // don't leave this test's schedule persisted.
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
