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

/// <summary>
/// FxSyncJob's own Quartz scheduling mechanics (spec-04 AC10, design decision 4: one scheduler,
/// <c>AddFxSyncJob</c> called after <c>AddPriceSyncJob</c>) — a shortened dev cron fires the real job
/// automatically with a trace span. Mirrors <see cref="PriceSyncSchedulingTests"/>'s own first fact;
/// the job's business logic is covered without any scheduling machinery in <see cref="FxSyncJobTests"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class FxSyncSchedulingTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // Explicit field: the primary constructor parameter is also passed to the base constructor
    // above, so referencing it directly elsewhere in this class would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    [Fact]
    public async Task AddFxSyncJob_OnShortenedDevCron_FiresAutomatically_WithTraceSpan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var seedDb = CreateDbContext();
        await seedDb.SeedCurrencyCatalogAsync(cancellationToken);
        foreach (var code in new[] { "EUR", "USD", "GBP", "CHF" })
        {
            await seedDb.SeedFxRateAsync($"{code}PLN", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30), 4m, cancellationToken);
        }

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FxSyncJob.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:marketdata-db"] = _containers.PostgresConnectionString;
        builder.Configuration["ConnectionStrings:rabbitmq"] = _containers.RabbitMqConnectionString;
        builder.Configuration["PriceSync:Cron"] = "0 0 0 1 1 ? 2099"; // effectively never — this test only cares about FX.
        builder.Configuration["FxSync:Cron"] = "0/2 * * * * ?"; // every 2s — proves "runs on schedule" fast.
        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(_containers.PostgresConnectionString));
        builder.Services.AddSingleton<IFxRateSource>(new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success(
        [
            new FxRateQuote("EURPLN", DateOnly.FromDateTime(DateTime.UtcNow), 4.30m),
            new FxRateQuote("USDPLN", DateOnly.FromDateTime(DateTime.UtcNow), 3.65m),
            new FxRateQuote("GBPPLN", DateOnly.FromDateTime(DateTime.UtcNow), 4.90m),
            new FxRateQuote("CHFPLN", DateOnly.FromDateTime(DateTime.UtcNow), 4.55m),
        ])));
        // FxSyncJob resolves IPublishEndpoint (spec-04 AC9) — the real job fires on this host's own
        // schedule, so it needs the same outbox wiring Program.cs gives it in production.
        builder.AddRabbitMqMessaging<HostApplicationBuilder, MarketDataDbContext>();
        builder.AddPriceSyncJob();
        builder.AddFxSyncJob();

        // Deliberately not disposed — see PriceSyncSchedulingTests's identical comment on the same
        // Quartz.Logging.LogProvider static-cache hazard.
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
            Assert.Contains(activities, a => a.OperationName == "FxSyncJob.Run");

            await using var db = CreateDbContext();
            Assert.True(await db.SyncRuns.AnyAsync(r => r.Kind == SyncRunKind.Fx, cancellationToken),
                "expected at least one automatic Fx-kind SyncRun row.");
        }
        finally
        {
            var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
            await scheduler.DeleteJob(FxSyncJob.Key, cancellationToken); // don't leave this test's schedule persisted.
            await host.StopAsync(cancellationToken);
        }
    }
}
