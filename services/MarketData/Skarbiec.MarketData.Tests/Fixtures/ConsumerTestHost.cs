using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Messaging;
using Skarbiec.MarketData.Sources;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests.Fixtures;

// A bus with no HTTP host: the real consumers run against RabbitMQ and the database, and the fake trigger records enqueues.
public static class ConsumerTestHost
{
    public static async Task MigrateAndResetAsync(SkarbiecContainersFixture containers)
    {
        await using var provider = Build(containers, out _);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MarketDataDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public static async Task RunAsync(
        SkarbiecContainersFixture containers,
        Func<ServiceProvider, FakeHistoryBackfillTrigger, Task> action,
        CancellationToken cancellationToken)
    {
        await using var provider = Build(containers, out var trigger);
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            // StartAsync does not wait for RabbitMQ to apply the bindings, so a publish right after it could be dropped.
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

            await action(provider, trigger);
        }
        finally
        {
            for (var i = hostedServices.Count - 1; i >= 0; i--)
            {
                await hostedServices[i].StopAsync(cancellationToken);
            }
        }
    }

    public static async Task<InstrumentUsage> WaitForUsageAsync(
        ServiceProvider provider, Guid instrumentId, CancellationToken cancellationToken, Func<InstrumentUsage, bool>? predicate = null)
    {
        predicate ??= _ => true;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentId, cancellationToken);

            if (usage is not null && predicate(usage))
            {
                return usage;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"No matching InstrumentUsage for instrument {instrumentId} within the deadline.");
    }

    public static async Task WaitForEnqueuedAsync(
        FakeHistoryBackfillTrigger trigger, Guid instrumentId, int count, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline && trigger.CountFor(instrumentId) < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        Assert.True(trigger.CountFor(instrumentId) >= count, "The backfill was not enqueued within the deadline.");
    }

    public static ServiceProvider Build(SkarbiecContainersFixture containers, out FakeHistoryBackfillTrigger trigger)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var fakeTrigger = new FakeHistoryBackfillTrigger();
        services.AddSingleton<IHistoryBackfillTrigger>(fakeTrigger);
        trigger = fakeTrigger;

        services.AddDbContext<MarketDataDbContext>(options => options.UseNpgsql(containers.PostgresConnectionString));

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            // No UseBusOutbox(): only the consumer-side inbox is under test.
            x.AddEntityFrameworkOutbox<MarketDataDbContext>(o => o.UsePostgres());

            x.AddConsumer<AssetPositionChangedConsumer>(typeof(TestAssetPositionChangedConsumerDefinition));
            x.AddConsumer<AssetRemovedConsumer>(typeof(TestAssetRemovedConsumerDefinition));
            x.AddConsumer<WatchlistItemAddedConsumer>(typeof(TestWatchlistItemAddedConsumerDefinition));
            x.AddConsumer<WatchlistItemRemovedConsumer>(typeof(TestWatchlistItemRemovedConsumerDefinition));

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(containers.RabbitMqConnectionString));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class TestAssetPositionChangedConsumerDefinition
        : IdempotentConsumerDefinition<AssetPositionChangedConsumer, MarketDataDbContext>
    {
        public TestAssetPositionChangedConsumerDefinition() => Endpoint(e => e.Name = "instrument-usage-position-changed-test");
    }

    private sealed class TestAssetRemovedConsumerDefinition
        : IdempotentConsumerDefinition<AssetRemovedConsumer, MarketDataDbContext>
    {
        public TestAssetRemovedConsumerDefinition() => Endpoint(e => e.Name = "instrument-usage-removed-test");
    }

    private sealed class TestWatchlistItemAddedConsumerDefinition
        : IdempotentConsumerDefinition<WatchlistItemAddedConsumer, MarketDataDbContext>
    {
        public TestWatchlistItemAddedConsumerDefinition() => Endpoint(e => e.Name = "instrument-usage-watchlist-added-test");
    }

    private sealed class TestWatchlistItemRemovedConsumerDefinition
        : IdempotentConsumerDefinition<WatchlistItemRemovedConsumer, MarketDataDbContext>
    {
        public TestWatchlistItemRemovedConsumerDefinition() => Endpoint(e => e.Name = "instrument-usage-watchlist-removed-test");
    }
}
