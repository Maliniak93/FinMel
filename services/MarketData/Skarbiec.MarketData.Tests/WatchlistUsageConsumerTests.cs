using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// Builds its own provider, with no HTTP host, through the shared consumer harness.
public sealed class WatchlistUsageConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    public ValueTask InitializeAsync() => new(ConsumerTestHost.MigrateAndResetAsync(containers));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Added_CountsWatcher()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var watchlistItemId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(WatchlistEvents.Added(watchlistItemId, instrumentId), cancellationToken);

            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 1);
            Assert.Equal(0, usage.AssetCount);
            Assert.NotEqual(default, usage.FirstUsedAt);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            var link = await db.InstrumentWatchLinks.SingleAsync(l => l.WatchlistItemId == watchlistItemId, cancellationToken);
            Assert.Equal(instrumentId, link.InstrumentId);
            Assert.False(link.IsRemoved);
        }, cancellationToken);
    }

    [Fact]
    public async Task Added_TwoUsers_CountsTwo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var firstWatchlistItemId = Guid.NewGuid();
        var secondWatchlistItemId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(WatchlistEvents.Added(firstWatchlistItemId, instrumentId), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 1);

            await bus.Publish(WatchlistEvents.Added(secondWatchlistItemId, instrumentId), cancellationToken);
            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 2);

            Assert.Equal(2, usage.WatchCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task Added_Redelivered_CountsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var watchlistItemId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = WatchlistEvents.Added(watchlistItemId, instrumentId);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 1);

            // Same MessageId: the inbox must skip the consumer body.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            var usage = await db.InstrumentUsages.SingleAsync(u => u.InstrumentId == instrumentId, cancellationToken);
            Assert.Equal(1, usage.WatchCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task Removed_Decrements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var watchlistItemId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(WatchlistEvents.Added(watchlistItemId, instrumentId), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 1);

            await bus.Publish(WatchlistEvents.Removed(watchlistItemId, instrumentId), cancellationToken);
            var usage = await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 0);
            Assert.Equal(0, usage.WatchCount);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            var link = await db.InstrumentWatchLinks.SingleAsync(l => l.WatchlistItemId == watchlistItemId, cancellationToken);
            Assert.True(link.IsRemoved);
        }, cancellationToken);
    }

    [Fact]
    public async Task Removed_Redelivered_CountsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var removedItemId = Guid.NewGuid();
        var remainingItemId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(WatchlistEvents.Added(removedItemId, instrumentId), cancellationToken);
            await bus.Publish(WatchlistEvents.Added(remainingItemId, instrumentId), cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 2);

            var messageId = Guid.NewGuid();
            var @event = WatchlistEvents.Removed(removedItemId, instrumentId);
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await ConsumerTestHost.WaitForUsageAsync(provider, instrumentId, cancellationToken, u => u.WatchCount == 1);

            // Same MessageId: the inbox must skip the consumer body, so the remaining watcher is not subtracted again.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            var usage = await db.InstrumentUsages.SingleAsync(u => u.InstrumentId == instrumentId, cancellationToken);
            Assert.Equal(1, usage.WatchCount);
        }, cancellationToken);
    }

    [Fact]
    public async Task Removed_BeforeAdded_IgnoresLateAdd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var watchlistItemId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(WatchlistEvents.Removed(watchlistItemId, instrumentId), cancellationToken);
            await WaitForTombstoneAsync(provider, watchlistItemId, cancellationToken);

            await bus.Publish(WatchlistEvents.Added(watchlistItemId, instrumentId), cancellationToken);

            // Nothing signals an ignored add, so give it time to land before asserting nothing changed.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

            // Removal alone never creates a usage row with a count; a row left at 0 is equally fine.
            var usage = await db.InstrumentUsages.SingleOrDefaultAsync(u => u.InstrumentId == instrumentId, cancellationToken);
            Assert.True(usage is null or { WatchCount: 0 });

            var link = await db.InstrumentWatchLinks.SingleAsync(l => l.WatchlistItemId == watchlistItemId, cancellationToken);
            Assert.True(link.IsRemoved);
        }, cancellationToken);
    }

    private static async Task WaitForTombstoneAsync(ServiceProvider provider, Guid watchlistItemId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            if (await db.InstrumentWatchLinks.AnyAsync(l => l.WatchlistItemId == watchlistItemId && l.IsRemoved, cancellationToken))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"No tombstoned watch link for watchlist item {watchlistItemId} within the deadline.");
    }
}
