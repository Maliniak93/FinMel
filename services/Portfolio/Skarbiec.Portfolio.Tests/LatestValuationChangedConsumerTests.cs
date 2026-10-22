using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Messaging;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioConsumers;

namespace Skarbiec.Portfolio.Tests;

// Builds its own provider, with no HTTP host, on a queue unique to this class.
public sealed class LatestValuationChangedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "latest-valuation-changed-consumer-test";

    private static readonly DateOnly BaselineDate = new(2026, 10, 15);
    private static readonly DateTimeOffset BaselineAt = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_NoReplica_StoresTheValuation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(
                new LatestValuationChanged
                {
                    UserId = userId,
                    Date = BaselineDate,
                    IsStale = false,
                    TotalPln = 122_000m,
                    ByAssetClass =
                    [
                        new() { AssetClass = AssetClass.Stock, ValuePln = 80_000m },
                        new() { AssetClass = AssetClass.Etf, ValuePln = 30_000m },
                        new() { AssetClass = AssetClass.PreciousMetal, ValuePln = 12_000m },
                    ],
                    ByGroup =
                    [
                        new()
                        {
                            GroupId = groupId,
                            ByAssetClass =
                            [
                                new() { AssetClass = AssetClass.Stock, ValuePln = 50_000m },
                                new() { AssetClass = AssetClass.Etf, ValuePln = 30_000m },
                            ],
                        },
                        new() { GroupId = null, ByAssetClass = [new() { AssetClass = AssetClass.Stock, ValuePln = 30_000m }] },
                        new() { GroupId = Guid.NewGuid(), ByAssetClass = [new() { AssetClass = AssetClass.PreciousMetal, ValuePln = 12_000m }] },
                    ],
                    OccurredAtUtc = BaselineAt,
                },
                cancellationToken);

            var replica = await WaitForValuationReplicaAsync(provider, userId, cancellationToken);

            Assert.Equal(userId, replica.UserId);
            Assert.Equal(BaselineDate, replica.Date);
            Assert.Equal(BaselineAt, replica.OccurredAtUtc);
            Assert.False(replica.IsStale);
            Assert.Equal(122_000m, replica.TotalPln);
            Assert.Equal(
                new Dictionary<AssetClass, decimal>
                {
                    [AssetClass.Stock] = 80_000m,
                    [AssetClass.Etf] = 30_000m,
                    [AssetClass.PreciousMetal] = 12_000m,
                },
                replica.ByAssetClass.ToDictionary(c => c.AssetClass, c => c.ValuePln));
            Assert.Equal(3, replica.ByGroup.Count);
            Assert.Equal(
                new Dictionary<AssetClass, decimal> { [AssetClass.Stock] = 50_000m, [AssetClass.Etf] = 30_000m },
                replica.ByGroup.Single(g => g.GroupId == groupId).ByAssetClass.ToDictionary(c => c.AssetClass, c => c.ValuePln));
            Assert.Equal(30_000m, replica.ByGroup.Single(g => g.GroupId is null).ByAssetClass.Single().ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_OlderDateOrOlderOccurrenceOnTheSameDate_LeavesTheReplicaUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(userId, BaselineDate, BaselineAt, 1_000m), cancellationToken);
            await WaitForValuationReplicaAsync(provider, userId, cancellationToken);

            await bus.Publish(Event(userId, BaselineDate.AddDays(-1), BaselineAt.AddHours(5), 2_000m, isStale: true), cancellationToken);
            await bus.Publish(Event(userId, BaselineDate, BaselineAt.AddHours(-1), 3_000m, isStale: true), cancellationToken);

            // Nothing signals an ignored event, so give them time to land before asserting nothing changed.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var replica = (await GetValuationReplicaAsync(containers, userId, cancellationToken))!;
            Assert.Equal(BaselineDate, replica.Date);
            Assert.Equal(BaselineAt, replica.OccurredAtUtc);
            Assert.Equal(1_000m, replica.TotalPln);
            Assert.False(replica.IsStale);
            var group = Assert.Single(replica.ByGroup);
            Assert.Null(group.GroupId);
            Assert.Equal(1_000m, group.ByAssetClass.Single().ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_NewerOccurrenceOnTheSameDateOrNewerDate_ReplacesTheValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(userId, BaselineDate, BaselineAt, 1_000m), cancellationToken);
            await WaitForValuationReplicaAsync(provider, userId, cancellationToken);

            await bus.Publish(Event(userId, BaselineDate, BaselineAt.AddHours(1), 2_000m, isStale: true), cancellationToken);
            var sameDay = await WaitForValuationReplicaAsync(provider, userId, cancellationToken, r => r.TotalPln == 2_000m);
            Assert.Equal(BaselineDate, sameDay.Date);
            Assert.True(sameDay.IsStale);

            var nextDay = BaselineDate.AddDays(1);
            await bus.Publish(Event(userId, nextDay, BaselineAt.AddDays(1), 3_000m), cancellationToken);
            var replica = await WaitForValuationReplicaAsync(provider, userId, cancellationToken, r => r.TotalPln == 3_000m);
            Assert.Equal(nextDay, replica.Date);
            Assert.False(replica.IsStale);
            Assert.Equal(3_000m, Assert.Single(replica.ByGroup).ByAssetClass.Single().ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdTwice_AppliesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();

            await bus.Publish(Event(userId, BaselineDate, BaselineAt, 1_000m), ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForValuationReplicaAsync(provider, userId, cancellationToken);

            // Same MessageId but a newer date: only a skipped consumer body leaves the first state.
            await bus.Publish(Event(userId, BaselineDate.AddDays(1), BaselineAt.AddDays(1), 9_000m), ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var replica = (await GetValuationReplicaAsync(containers, userId, cancellationToken))!;
            Assert.Equal(BaselineDate, replica.Date);
            Assert.Equal(1_000m, replica.TotalPln);
        }, cancellationToken);
    }

    private static LatestValuationChanged Event(
        Guid userId, DateOnly date, DateTimeOffset occurredAtUtc, decimal totalPln, bool isStale = false) => new()
        {
            UserId = userId,
            Date = date,
            IsStale = isStale,
            TotalPln = totalPln,
            ByAssetClass = [new() { AssetClass = AssetClass.Stock, ValuePln = totalPln }],
            ByGroup = [new() { GroupId = null, ByAssetClass = [new() { AssetClass = AssetClass.Stock, ValuePln = totalPln }] }],
            OccurredAtUtc = occurredAtUtc,
        };

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x => x.AddConsumer<LatestValuationChangedConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<LatestValuationChangedConsumer, PortfolioDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
