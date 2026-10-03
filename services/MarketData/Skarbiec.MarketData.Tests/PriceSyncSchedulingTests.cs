using System.Collections.Concurrent;
using System.Diagnostics;
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

[Collection(TestingDefaults.CollectionName)]
public sealed class PriceSyncSchedulingTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

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
        builder.Configuration["ConnectionStrings:rabbitmq"] = _containers.RabbitMqConnectionString;
        builder.Configuration["PriceSync:Cron"] = "0/2 * * * * ?";
        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(_containers.PostgresConnectionString));
        builder.Services.AddSingleton<IFxRateSource>(new NoOpFxRateSource());
        // The real job fires on this host's schedule and resolves IPublishEndpoint, so it needs the outbox wiring.
        builder.AddRabbitMqMessaging<HostApplicationBuilder, MarketDataDbContext>();
        builder.AddPriceSyncJob();

        // Deliberately not disposed: Quartz's LogProvider caches this host's ILoggerFactory in a process-wide static.
        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && activities.IsEmpty)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            Assert.NotEmpty(activities);
            Assert.Contains(activities, a => a.OperationName == "PriceSyncJob.Run");

            await using var db = CreateDbContext();
            Assert.True(await db.SyncRuns.AnyAsync(cancellationToken), "expected at least one automatic SyncRun row.");
        }
        finally
        {
            var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
            await scheduler.DeleteJob(PriceSyncJob.Key, cancellationToken);
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
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
            .Build();

        await schedulerA.ScheduleJob(jobDetail, trigger, cancellationToken: cancellationToken);
        await schedulerA.Start(cancellationToken);
        var originalNextFireTime = (await schedulerA.GetTrigger(triggerKey, cancellationToken))!.NextFireTimeUtc;

        // Simulate a process restart: stop this instance without unscheduling anything.
        await schedulerA.Shutdown(waitForJobsToComplete: false, cancellationToken);

        var schedulerB = await BuildPersistentSchedulerAsync();
        await schedulerB.Start(cancellationToken);
        try
        {
            var survivedTrigger = await schedulerB.GetTrigger(triggerKey, cancellationToken);

            Assert.NotNull(survivedTrigger);
            Assert.Equal(originalNextFireTime, survivedTrigger.NextFireTimeUtc);
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
        Assert.NotEqual(schedulerA.SchedulerInstanceId, schedulerB.SchedulerInstanceId);

        var jobDetail = JobBuilder.Create<ProbeJob>().WithIdentity(jobKey).StoreDurably()
            .UsingJobData("stateKey", stateKey).Build();
        var trigger = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(2))
            .Build();
        await schedulerA.ScheduleJob(jobDetail, trigger, cancellationToken: cancellationToken);

        // Both instances live before the fire time: the overlap DisallowConcurrentExecution and clustering protect.
        await schedulerA.Start(cancellationToken);
        await schedulerB.Start(cancellationToken);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline && Volatile.Read(ref state.ExecutionCount) == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            Assert.Equal(1, state.ExecutionCount);
            Assert.Equal(1, state.MaxConcurrentExecutions);
        }
        finally
        {
            await schedulerA.DeleteJob(jobKey, cancellationToken);
            await schedulerA.Shutdown(waitForJobsToComplete: false, cancellationToken);
            await schedulerB.Shutdown(waitForJobsToComplete: false, cancellationToken);
        }
    }

    // Production's store on a standalone scheduler; the shorter clustering check-in is the only test override.
    private async Task<IScheduler> BuildPersistentSchedulerAsync()
    {
        var connectionString = _containers.PostgresConnectionString;
        await QuartzStore.EnsureSchemaAsync(connectionString, TestContext.Current.CancellationToken);

        return await QuartzSchedulerBuilder.Create(q =>
            {
                q.UseMarketDataStore(connectionString, cluster => cluster.CheckinInterval = TimeSpan.FromMilliseconds(500));
                q.ConfigureScheduler(scheduler => scheduler.InstanceName = "skarbiec-scheduling-tests");
            })
            .BuildScheduler(TestContext.Current.CancellationToken);
    }
}

// Looked up by a JobDataMap key: Quartz's classic job factory needs a parameterless constructor.
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

// No DI dependencies, so Quartz's classic JobBuilder API can build it against a bare scheduler.
[DisallowConcurrentExecution]
public sealed class ProbeJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
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
                await Task.Delay(state.HoldFor, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref state.ConcurrentExecutions);
            Interlocked.Increment(ref state.ExecutionCount);
        }
    }
}
