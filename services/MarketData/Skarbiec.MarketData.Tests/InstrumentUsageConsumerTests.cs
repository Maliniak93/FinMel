using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// Builds its own provider, with no HTTP host, through the shared consumer harness.
public sealed class InstrumentUsageConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    public ValueTask InitializeAsync() => new(ConsumerTestHost.MigrateAndResetAsync(containers));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_FirstAssetForInstrument_SetsUsageToOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(PositionChanged(assetId, instrumentId, version: 0), cancellationToken);

            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken);

            Assert.Equal(1, usage.AssetCount);
            Assert.NotEqual(default, usage.FirstUsedAt);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SecondAssetForSameInstrument_UsageTwo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(PositionChanged(firstAssetId, instrumentId, version: 0), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            await bus.Publish(PositionChanged(secondAssetId, instrumentId, version: 0), cancellationToken);
            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 2);

            Assert.Equal(2, usage.AssetCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_AssetRemoved_DecrementsUsage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(PositionChanged(firstAssetId, instrumentId, version: 0), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);
            await bus.Publish(PositionChanged(secondAssetId, instrumentId, version: 0), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 2);

            await bus.Publish(new AssetRemoved
            {
                AssetId = firstAssetId,
                AccountId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);
            Assert.Equal(1, usage.AssetCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task AssetRemoved_KeepsWatchCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(WatchlistEvents.Added(Guid.NewGuid(), instrumentId), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 1);

            await bus.Publish(PositionChanged(assetId, instrumentId, version: 0), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            await bus.Publish(new AssetRemoved
            {
                AssetId = assetId,
                AccountId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 0);
            Assert.Equal(0, usage.AssetCount);
            Assert.Equal(1, usage.WatchCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdDeliveredTwice_UsageCountedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = PositionChanged(assetId, instrumentId, version: 0);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            // Same MessageId: the inbox must skip the consumer body.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
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

        await ConsumerTestHost.RunAsync(containers, async (provider, trigger) =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(PositionChanged(assetId, instrumentA, version: 5), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentA, cancellationToken, u => u.AssetCount == 1);

            // The late event names another instrument, so only the Version guard keeps the state below.
            await bus.Publish(PositionChanged(assetId, instrumentB, version: 3), cancellationToken);

            // Nothing signals an ignored event, so give it time to land before asserting nothing changed.
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

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(PositionChanged(assetId, instrumentA, version: 0), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentA, cancellationToken, u => u.AssetCount == 1);

            await bus.Publish(PositionChanged(assetId, instrumentB, version: 1), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentB, cancellationToken, u => u.AssetCount == 1);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

            var usageA = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentA, cancellationToken);
            Assert.True(usageA is null or { AssetCount: 0 });

            var usageB = await db.InstrumentUsages.SingleAsync(u => u.InstrumentId == instrumentB, cancellationToken);
            Assert.Equal(1, usageB.AssetCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task FirstHolding_EnqueuesFromFirstTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, trigger) =>
        {
            await SeedInstrumentAsync(provider, instrumentId, historyCoveredFrom: null, cancellationToken);

            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                PositionChanged(Guid.NewGuid(), instrumentId, version: 0, firstTransactionDate: new DateOnly(2023, 5, 2)),
                cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            await ConsumerTestHost.WaitForEnqueuedAsync(trigger, instrumentId, 1, cancellationToken);
            Assert.Equal([new DateOnly(2023, 5, 2)], trigger.FromsFor(instrumentId));
        }, cancellationToken);
    }

    [Fact]
    public async Task OlderTransaction_EnqueuesOnlyWhenEarlier()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, trigger) =>
        {
            await SeedInstrumentAsync(provider, instrumentId, historyCoveredFrom: new DateOnly(2023, 5, 2), cancellationToken);

            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                PositionChanged(Guid.NewGuid(), instrumentId, version: 0, firstTransactionDate: new DateOnly(2024, 1, 1)),
                cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            // Nothing signals a skipped enqueue, so give it time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            Assert.Equal(0, trigger.CountFor(instrumentId));

            await bus.Publish(
                PositionChanged(Guid.NewGuid(), instrumentId, version: 0, firstTransactionDate: new DateOnly(2022, 11, 15)),
                cancellationToken);
            await ConsumerTestHost.WaitForEnqueuedAsync(trigger, instrumentId, 1, cancellationToken);

            Assert.Equal([new DateOnly(2022, 11, 15)], trigger.FromsFor(instrumentId));
        }, cancellationToken);
    }

    [Fact]
    public async Task NoTransactions_CountsUsage_NoBackfill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, trigger) =>
        {
            await SeedInstrumentAsync(provider, instrumentId, historyCoveredFrom: null, cancellationToken);

            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                PositionChanged(Guid.NewGuid(), instrumentId, version: 0, firstTransactionDate: null),
                cancellationToken);
            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.AssetCount == 1);

            // Nothing signals a skipped enqueue, so give it time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Equal(1, usage.AssetCount);
            Assert.Equal(0, trigger.CountFor(instrumentId));
        }, cancellationToken);
    }

    private static async Task SeedInstrumentAsync(
        ServiceProvider provider, Guid instrumentId, DateOnly? historyCoveredFrom, CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        db.Instruments.Add(new Instrument
        {
            Id = instrumentId,
            Ticker = $"T{instrumentId:N}"[..12],
            Name = "Seeded",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
            HistoryCoveredFrom = historyCoveredFrom,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AssetPositionChanged PositionChanged(
        Guid assetId, Guid instrumentId, long version, DateOnly? firstTransactionDate = null) => new()
        {
            AssetId = assetId,
            AccountId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            AssetClass = AssetClass.Stock,
            ValuationMode = AssetValuationMode.Market,
            InstrumentId = instrumentId,
            Currency = "USD",
            Quantity = 1m,
            QuoteUnitsPerQuantity = 1m,
            ClassHistory = [],
            QuantityHistory = [],
            IsArchived = false,
            Version = version,
            FirstTransactionDate = firstTransactionDate,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };
}
