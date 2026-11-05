using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.TriggerSync;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.MfBonds;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests.Fixtures;

internal static class SyncHost
{
    // Fires at the top of every hour, so the last cron slot is never more than an hour behind and a run seeded just now is after it.
    public const string HourlyCron = "0 0 * ? * *";

    public static async Task<IHost> StartAsync(
        SkarbiecContainersFixture containers,
        IFxRateSource fxSource,
        IMfBondSource bondSource,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? settings = null,
        IEnumerable<IPriceSource>? priceSources = null,
        FakeFxHistoryBackfillTrigger? fxBackfillTrigger = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:marketdata-db"] = containers.PostgresConnectionString;
        builder.Configuration["ConnectionStrings:rabbitmq"] = containers.RabbitMqConnectionString;
        builder.Configuration["PriceSync:Cron"] = HourlyCron;
        builder.Configuration["FxSync:Cron"] = HourlyCron;
        builder.Configuration["BondCatalogSync:Cron"] = HourlyCron;
        builder.Configuration["StartupSync:MinRetryInterval"] = "00:10:00";
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.Configuration[key] = value;
        }

        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(containers.PostgresConnectionString));
        builder.Services.AddScoped<TriggerSyncHandler>();
        builder.Services.AddSingleton(fxSource);
        builder.Services.AddSingleton(bondSource);
        builder.Services.AddSingleton<IHistoryBackfillTrigger>(new FakeHistoryBackfillTrigger());
        builder.Services.AddSingleton<IFxHistoryBackfillTrigger>(fxBackfillTrigger ?? new FakeFxHistoryBackfillTrigger());
        foreach (var priceSource in priceSources ?? [])
        {
            builder.Services.AddSingleton(priceSource);
        }

        builder.AddRabbitMqMessaging<HostApplicationBuilder, MarketDataDbContext>();
        builder.AddPriceSyncJob();
        builder.AddFxSyncJob();
        builder.AddBondCatalogSyncJob();
        builder.AddStartupSync();

        // Deliberately not disposed: Quartz's LogProvider caches this host's ILoggerFactory in a process-wide static.
        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        return host;
    }

    public static async Task CleanUpAsync(IHost host, CancellationToken cancellationToken)
    {
        var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken);
        await scheduler.Clear(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
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

        Assert.Fail("The condition was not met in 30 seconds.");
    }
}
