using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.MfBonds;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class BondCatalogStartupTriggerTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    [Fact]
    public async Task Start_WithEmptyCatalog_TriggersOneBondCatalogRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = await StartHostAsync(FakeMfBondSource.FromFixture(), cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            await WaitUntilAsync(
                () => db.SyncRuns.AsNoTracking().AnyAsync(
                    r => r.Kind == SyncRunKind.BondCatalog && r.Status == SyncRunStatus.Completed, cancellationToken),
                cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            Assert.Equal(1, await db.SyncRuns.CountAsync(r => r.Kind == SyncRunKind.BondCatalog, cancellationToken));
            Assert.True(await db.BondSeries.AnyAsync(cancellationToken));
        }
        finally
        {
            await CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task Start_WithNonEmptyCatalog_TriggersNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedBondSeriesAsync("EDO1036", TreasuryBondType.Edo, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), cancellationToken);
        }

        var source = FakeMfBondSource.FromFixture();
        var host = await StartHostAsync(source, cancellationToken);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);

            await using var db = CreateDbContext();
            Assert.Equal(0, await db.SyncRuns.CountAsync(r => r.Kind == SyncRunKind.BondCatalog, cancellationToken));
            Assert.Equal(0, source.FetchCount);
        }
        finally
        {
            await CleanUpAsync(host, cancellationToken);
        }
    }

    private async Task<IHost> StartHostAsync(IMfBondSource source, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:marketdata-db"] = _containers.PostgresConnectionString;
        builder.Configuration["ConnectionStrings:rabbitmq"] = _containers.RabbitMqConnectionString;
        builder.Configuration["BondCatalogSync:Cron"] = "0 0 0 1 1 ? 2099";
        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(_containers.PostgresConnectionString));
        builder.Services.AddSingleton<IFxRateSource>(new NoOpFxRateSource());
        builder.Services.AddSingleton(source);
        builder.AddRabbitMqMessaging<HostApplicationBuilder, MarketDataDbContext>();
        builder.AddPriceSyncJob();
        builder.AddBondCatalogSyncJob();

        // Deliberately not disposed: Quartz's LogProvider caches this host's ILoggerFactory in a process-wide static.
        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        return host;
    }

    private static async Task CleanUpAsync(IHost host, CancellationToken cancellationToken)
    {
        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        await scheduler.Clear(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException("The condition was not met in 30 seconds.");
    }
}
