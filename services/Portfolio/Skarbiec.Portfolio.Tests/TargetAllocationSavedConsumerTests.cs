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
public sealed class TargetAllocationSavedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "target-allocation-saved-consumer-test";

    private static readonly DateTimeOffset SavedAt = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_NoReplica_StoresGroupsInOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(
                new TargetAllocationSaved
                {
                    UserId = userId,
                    SavedAtUtc = SavedAt,
                    Groups =
                    [
                        new() { Name = "Akcje/ETF", AssetClasses = [AssetClass.Stock, AssetClass.Etf], TargetPercent = 50m, BandPp = 5m },
                        new() { Name = "Obligacje", AssetClasses = [AssetClass.Bond, AssetClass.Deposit], TargetPercent = 40m, BandPp = 5m },
                        new() { Name = "Kruszce", AssetClasses = [AssetClass.PreciousMetal], TargetPercent = 10m, BandPp = 5m },
                    ],
                },
                cancellationToken);

            var replica = await WaitForTargetReplicaAsync(provider, userId, cancellationToken);

            Assert.Equal(userId, replica.UserId);
            Assert.Equal(SavedAt, replica.SavedAtUtc);
            Assert.Equal(["Akcje/ETF", "Obligacje", "Kruszce"], replica.Groups.Select(g => g.Name).ToList());
            Assert.Equal([50m, 40m, 10m], replica.Groups.Select(g => g.TargetPercent).ToList());
            Assert.Equal([AssetClass.Stock, AssetClass.Etf], replica.Groups.First().AssetClasses.ToList());
            Assert.All(replica.Groups, g => Assert.Equal(5m, g.BandPp));
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

            await bus.Publish(SingleGroupEvent(userId, SavedAt, "First", AssetClass.Stock), ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForTargetReplicaAsync(provider, userId, cancellationToken);

            // Same MessageId, a newer SavedAtUtc and other groups: only a skipped consumer body leaves the first state.
            await bus.Publish(SingleGroupEvent(userId, SavedAt.AddHours(1), "Second", AssetClass.Bond), ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var replica = (await GetTargetReplicaAsync(containers, userId, cancellationToken))!;
            Assert.Equal(SavedAt, replica.SavedAtUtc);
            Assert.Equal("First", Assert.Single(replica.Groups).Name);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_OlderSavedAtUtc_KeepsTheNewerGroups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(SingleGroupEvent(userId, SavedAt, "Newer", AssetClass.Stock), cancellationToken);
            await WaitForTargetReplicaAsync(provider, userId, cancellationToken);

            await bus.Publish(SingleGroupEvent(userId, SavedAt.AddMinutes(-30), "Older", AssetClass.Bond), cancellationToken);

            // Nothing signals an ignored event, so give it time to land before asserting nothing changed.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var replica = (await GetTargetReplicaAsync(containers, userId, cancellationToken))!;
            Assert.Equal(SavedAt, replica.SavedAtUtc);
            Assert.Equal("Newer", Assert.Single(replica.Groups).Name);
        }, cancellationToken);
    }

    private static TargetAllocationSaved SingleGroupEvent(Guid userId, DateTimeOffset savedAtUtc, string name, AssetClass assetClass) => new()
    {
        UserId = userId,
        SavedAtUtc = savedAtUtc,
        Groups = [new() { Name = name, AssetClasses = [assetClass], TargetPercent = 100m, BandPp = 5m }],
    };

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x => x.AddConsumer<TargetAllocationSavedConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<TargetAllocationSavedConsumer, PortfolioDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
