using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Quartz scheduling mechanics (T2.6 AC): a shortened dev cron fires the real job automatically with
/// a trace span, a persisted schedule survives a scheduler restart, and two clustered scheduler
/// instances that are briefly both live (the restart/rolling-deploy handoff window) share one fire
/// exactly once — never lost, never duplicated. <see cref="PriceSyncJobTests"/> covers the job's own
/// business logic without any of this scheduling machinery.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PriceSyncSchedulingTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // Explicit field: the primary constructor parameter is also passed to the base constructor
    // above, so referencing it directly elsewhere in this class would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string TablePrefix = "quartz.qrtz_"; // must match MarketDataDbContext's modelBuilder.AddQuartz schema/prefix.

    [Fact]
    public async Task AddPriceSyncJob_OnShortenedDevCron_FiresAutomatically_WithTraceSpan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PriceSyncJob.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:marketdata-db"] = _containers.PostgresConnectionString;
        builder.Configuration["PriceSync:Cron"] = "0/2 * * * * ?"; // every 2s — proves "runs on schedule" fast.
        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(_containers.PostgresConnectionString));
        builder.Services.AddSingleton<IFxRateSource>(new NoOpFxRateSource());
        builder.AddPriceSyncJob();

        // Deliberately not disposed: Quartz.Logging.LogProvider caches this host's ILoggerFactory
        // in a process-wide static once AddQuartzHostedService starts it. Disposing the host here
        // would dispose that factory and poison every later Quartz scheduler in this test process
        // (including the classic-API ones in this same class) with an ObjectDisposedException on
        // their first log call. The host is stopped (releasing the scheduler/DB connections) but
        // left undisposed for the remainder of the test process — harmless, since the process exits
        // once the test run finishes.
        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && activities.IsEmpty)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            Assert.NotEmpty(activities); // the job fired on its own, without a manual trigger.
            Assert.Contains(activities, a => a.OperationName == "PriceSyncJob.Run");

            await using var db = CreateDbContext();
            Assert.True(await db.SyncRuns.AnyAsync(cancellationToken), "expected at least one automatic SyncRun row.");
        }
        finally
        {
            var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
            await scheduler.DeleteJob(PriceSyncJob.Key, cancellationToken); // don't leave this test's schedule persisted.
            await host.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Schedule_SurvivesSchedulerRestart_TriggerNotLost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobKey = new JobKey($"probe-{Guid.NewGuid()}", "test");
        var triggerKey = new TriggerKey($"probe-trigger-{Guid.NewGuid()}", "test");
        var stateKey = ProbeJobRegistry.Register(new ProbeState());

        var schedulerA = await BuildPersistentSchedulerAsync();
        var jobDetail = JobBuilder.Create<ProbeJob>().WithIdentity(jobKey).StoreDurably()
            .UsingJobData("stateKey", stateKey).Build();
        var trigger = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(5)) // far enough out it can't fire during this test.
            .Build();

        await schedulerA.ScheduleJob(jobDetail, trigger, cancellationToken);
        await schedulerA.Start(cancellationToken);
        var originalNextFireTime = (await schedulerA.GetTrigger(triggerKey, cancellationToken))!.GetNextFireTimeUtc();

        // Simulate a process restart: stop this instance without unscheduling anything.
        await schedulerA.Shutdown(waitForJobsToComplete: false, cancellationToken);

        var schedulerB = await BuildPersistentSchedulerAsync();
        await schedulerB.Start(cancellationToken);
        try
        {
            var survivedTrigger = await schedulerB.GetTrigger(triggerKey, cancellationToken);

            Assert.NotNull(survivedTrigger);
            Assert.Equal(originalNextFireTime, survivedTrigger.GetNextFireTimeUtc());
        }
        finally
        {
            await schedulerB.DeleteJob(jobKey, cancellationToken);
            await schedulerB.Shutdown(waitForJobsToComplete: false, cancellationToken);
        }
    }

    [Fact]
    public async Task ClusteredSchedulers_ShareOneFire_ExactlyOnce_NeverLostNeverDuplicated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var jobKey = new JobKey($"probe-{Guid.NewGuid()}", "test");
        var triggerKey = new TriggerKey($"probe-trigger-{Guid.NewGuid()}", "test");
        var state = new ProbeState { HoldFor = TimeSpan.FromMilliseconds(500) };
        var stateKey = ProbeJobRegistry.Register(state);

        var schedulerA = await BuildPersistentSchedulerAsync();
        var schedulerB = await BuildPersistentSchedulerAsync();

        var jobDetail = JobBuilder.Create<ProbeJob>().WithIdentity(jobKey).StoreDurably()
            .UsingJobData("stateKey", stateKey).Build();
        var trigger = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(2))
            .Build();
        await schedulerA.ScheduleJob(jobDetail, trigger, cancellationToken);

        // Both "instances" live before the fire time — the exact restart/rolling-deploy overlap
        // window DisallowConcurrentExecution + clustering exist to protect (T2.6 AC).
        await schedulerA.Start(cancellationToken);
        await schedulerB.Start(cancellationToken);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline && Volatile.Read(ref state.ExecutionCount) == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            Assert.Equal(1, state.ExecutionCount); // not lost (someone fired it) ...
            Assert.Equal(1, state.MaxConcurrentExecutions); // ... and not duplicated (only one node at a time).
        }
        finally
        {
            await schedulerA.DeleteJob(jobKey, cancellationToken);
            await schedulerA.Shutdown(waitForJobsToComplete: false, cancellationToken);
            await schedulerB.Shutdown(waitForJobsToComplete: false, cancellationToken);
        }
    }

    private async Task<IScheduler> BuildPersistentSchedulerAsync()
    {
        var scheduler = await SchedulerBuilder.Create(id: "AUTO", name: "skarbiec-scheduling-tests")
            .UsePersistentStore(store =>
            {
                store.UsePostgres(c =>
                {
                    c.ConnectionString = _containers.PostgresConnectionString;
                    c.TablePrefix = TablePrefix;
                });
                store.UseSystemTextJsonSerializer();
                store.UseClustering(cluster => cluster.CheckinInterval = TimeSpan.FromMilliseconds(500));
            })
            .BuildScheduler();

        return scheduler;
    }

    private sealed class NoOpFxRateSource : IFxRateSource
    {
        public TimeSpan RequestDelay => TimeSpan.Zero;

        public Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
            IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken) =>
            Task.FromResult(PriceFetchResult<FxRateQuote>.NoData());

        public Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
            string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult(PriceFetchResult<FxRateQuote>.NoData());
    }
}

/// <summary>Per-run state for <see cref="ProbeJob"/>, looked up by a key threaded through the
/// trigger's <c>JobDataMap</c> — Quartz's classic (non-DI) job factory needs a public parameterless
/// constructor, so state can't be constructor-injected the way <see cref="PriceSyncJob"/>'s can.</summary>
internal sealed class ProbeState
{
    public int ExecutionCount;
    public int ConcurrentExecutions;
    public int MaxConcurrentExecutions;
    public TimeSpan HoldFor;
}

internal static class ProbeJobRegistry
{
    private static readonly ConcurrentDictionary<string, ProbeState> States = new();

    public static string Register(ProbeState state)
    {
        var key = Guid.NewGuid().ToString();
        States[key] = state;
        return key;
    }

    public static ProbeState Get(string key) => States[key];
}

/// <summary>Minimal <see cref="IJob"/> standing in for <see cref="PriceSyncJob"/> in scheduling-only
/// tests (cron firing, restart/cluster safety) — carries no DI dependencies, so it can be built via
/// Quartz's classic <see cref="JobBuilder"/> API against a bare <see cref="IScheduler"/>.</summary>
[DisallowConcurrentExecution]
public sealed class ProbeJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var state = ProbeJobRegistry.Get(context.MergedJobDataMap.GetString("stateKey")!);

        var concurrent = Interlocked.Increment(ref state.ConcurrentExecutions);
        int observedMax;
        do
        {
            observedMax = state.MaxConcurrentExecutions;
        }
        while (concurrent > observedMax
            && Interlocked.CompareExchange(ref state.MaxConcurrentExecutions, concurrent, observedMax) != observedMax);

        try
        {
            if (state.HoldFor > TimeSpan.Zero)
            {
                await Task.Delay(state.HoldFor, context.CancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref state.ConcurrentExecutions);
            Interlocked.Increment(ref state.ExecutionCount);
        }
    }
}
