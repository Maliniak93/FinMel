using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Messaging;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// <c>AssetPositionChanged</c>/<c>AssetRemoved</c> in, <see cref="AssetInstrumentLink"/> and the
/// derived <see cref="InstrumentUsage"/> row out (spec-04 AC11-16, design decisions 9-12) — idempotent
/// by <c>MessageId</c>, order-safe by <c>Version</c>, and the trigger for
/// <see cref="IHistoryBackfillTrigger.EnqueueAsync"/> on a 0-to-1 usage transition. Builds its own
/// provider (no HTTP host needed) on a queue name unique to this test class, mirroring Reporting's
/// <c>AssetPositionChangedConsumerTests</c>/<c>DailyPricesSyncedConsumerTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class InstrumentUsageConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PositionChangedQueueName = "instrument-usage-position-changed-test";
    private const string RemovedQueueName = "instrument-usage-removed-test";

    public async ValueTask InitializeAsync()
    {
        await using var provider = BuildProvider(out _);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MarketDataDbContext>().Database.MigrateAsync();

        await containers.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_FirstAssetForInstrument_SetsUsageToOne_AndEnqueuesBackfillOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await RunConsumersAsync(async (provider, trigger) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(PositionChanged(assetId, instrumentId, version: 0), cancellationToken);

            var usage = await WaitForUsageAsync(provider, instrumentId, cancellationToken);

            Assert.Equal(1, usage.AssetCount);
            Assert.NotEqual(default, usage.FirstUsedAt);
            Assert.Equal(1, trigger.CountFor(instrumentId));
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SecondAssetForSameInstrument_UsageTwo_NoSecondBackfill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();

        await RunConsumersAsync(async (provider, trigger) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(PositionChanged(firstAssetId, instrumentId, version: 0), cancellationToken);
            await WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            await bus.Publish(PositionChanged(secondAssetId, instrumentId, version: 0), cancellationToken);
            var usage = await WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 2);

            Assert.Equal(2, usage.AssetCount);
            Assert.Equal(1, trigger.CountFor(instrumentId)); // still only the first asset's 0->1 transition.
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_AssetRemoved_DecrementsUsage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();

        await RunConsumersAsync(async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(PositionChanged(firstAssetId, instrumentId, version: 0), cancellationToken);
            await WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);
            await bus.Publish(PositionChanged(secondAssetId, instrumentId, version: 0), cancellationToken);
            await WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 2);

            await bus.Publish(new AssetRemoved
            {
                AssetId = firstAssetId,
                PortfolioId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                OccurredAtUtc = DateTimeOffset.UtcNow,
                CascadedFromPortfolio = false,
            }, cancellationToken);

            var usage = await WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);
            Assert.Equal(1, usage.AssetCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdDeliveredTwice_UsageCountedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await RunConsumersAsync(async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = PositionChanged(assetId, instrumentId, version: 0);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            // Redeliver: same MessageId, same content. The inbox (InboxState, keyed on MessageId +
            // ConsumerId) must recognize it and skip the consumer body entirely.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // No signal to wait on for "it was skipped" — give a genuine redelivery time to land
            // before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            var usage = await db.InstrumentUsages.SingleAsync(u => u.InstrumentId == instrumentId, cancellationToken);
            Assert.Equal(1, usage.AssetCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_OutOfOrderVersion_Ignored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentA = Guid.NewGuid();
        var instrumentB = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await RunConsumersAsync(async (provider, trigger) =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(PositionChanged(assetId, instrumentA, version: 5), cancellationToken);
            await WaitForUsageAsync(provider, instrumentA, cancellationToken, u => u.AssetCount == 1);

            // The late event points at a different instrument: applied, it would move the count from
            // A to B and enqueue a backfill for B — so only the Version guard keeps the state below.
            await bus.Publish(PositionChanged(assetId, instrumentB, version: 3), cancellationToken);

            // No signal to wait on for "it was ignored" — give a genuine (mis-ordered) delivery time
            // to land before asserting the stored state was left untouched.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

            var usageA = await db.InstrumentUsages.SingleAsync(u => u.InstrumentId == instrumentA, cancellationToken);
            Assert.Equal(1, usageA.AssetCount);

            var usageB = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentB, cancellationToken);
            Assert.True(usageB is null or { AssetCount: 0 });
            Assert.Equal(0, trigger.CountFor(instrumentB));

            var link = await db.AssetInstrumentLinks.SingleAsync(l => l.AssetId == assetId, cancellationToken);
            Assert.Equal(5, link.Version);
            Assert.Equal(instrumentA, link.InstrumentId);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_InstrumentSwitchedOnUpdate_MovesTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentA = Guid.NewGuid();
        var instrumentB = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await RunConsumersAsync(async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(PositionChanged(assetId, instrumentA, version: 0), cancellationToken);
            await WaitForUsageAsync(provider, instrumentA, cancellationToken, u => u.AssetCount == 1);

            await bus.Publish(PositionChanged(assetId, instrumentB, version: 1), cancellationToken);
            await WaitForUsageAsync(provider, instrumentB, cancellationToken, u => u.AssetCount == 1);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

            var usageA = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentA, cancellationToken);
            Assert.True(usageA is null or { AssetCount: 0 });

            var usageB = await db.InstrumentUsages.SingleAsync(u => u.InstrumentId == instrumentB, cancellationToken);
            Assert.Equal(1, usageB.AssetCount);
        }, cancellationToken);
    }

    private static AssetPositionChanged PositionChanged(Guid assetId, Guid instrumentId, long version) => new()
    {
        AssetId = assetId,
        PortfolioId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        AssetClass = AssetClass.Stock,
        ValuationMode = AssetValuationMode.Market,
        InstrumentId = instrumentId,
        Currency = "USD",
        Quantity = 1m,
        PortfolioIsArchived = false,
        Version = version,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };

    private async Task RunConsumersAsync(
        Func<ServiceProvider, FakeHistoryBackfillTrigger, Task> action, CancellationToken cancellationToken)
    {
        await using var provider = BuildProvider(out var trigger);
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            // See Reporting's AssetPositionChangedConsumerTests: a publish fired immediately after
            // StartAsync can race the exchange->queue binding on a fresh queue and be dropped.
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

    private static async Task<InstrumentUsage> WaitForUsageAsync(
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

    private ServiceProvider BuildProvider(out FakeHistoryBackfillTrigger trigger)
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

            // No UseBusOutbox() here: this test only exercises the consumer-side inbox, never
            // IBus/IPublishEndpoint from a DI scope, so the producer-side bus outbox and its
            // background delivery poller would be dead weight.
            x.AddEntityFrameworkOutbox<MarketDataDbContext>(o => o.UsePostgres());

            x.AddConsumer<AssetPositionChangedConsumer>(typeof(TestAssetPositionChangedConsumerDefinition));
            x.AddConsumer<AssetRemovedConsumer>(typeof(TestAssetRemovedConsumerDefinition));

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
        public TestAssetPositionChangedConsumerDefinition() => Endpoint(e => e.Name = PositionChangedQueueName);
    }

    private sealed class TestAssetRemovedConsumerDefinition
        : IdempotentConsumerDefinition<AssetRemovedConsumer, MarketDataDbContext>
    {
        public TestAssetRemovedConsumerDefinition() => Endpoint(e => e.Name = RemovedQueueName);
    }
}
