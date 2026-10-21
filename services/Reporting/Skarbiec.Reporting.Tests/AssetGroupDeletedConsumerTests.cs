using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// Builds its own provider, with no HTTP host, on a queue unique to this class.
public sealed class AssetGroupDeletedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "asset-group-deleted-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_RemovesAllocationOfThatUserAndGroupOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();
        await SeedAsync(cancellationToken, (userId, groupId), (userId, otherGroupId), (otherUserId, groupId));

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(DeletedEvent(userId, groupId), cancellationToken);

            await WaitForAsync(
                provider,
                (db, ct) => db.GroupTargetAllocations.IgnoreQueryFilters().AnyAsync(a => a.UserId == userId && a.GroupId == groupId, ct),
                stillExists => !stillExists,
                "Deleting the group allocation",
                cancellationToken);

            var expected = new[] { (userId, otherGroupId), (otherUserId, groupId) }.OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToList();
            Assert.Equal(expected, await ReadKeysAsync(cancellationToken));
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageTwiceAndGroupWithoutAllocation_ChangeNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var keptGroupId = Guid.NewGuid();
        await SeedAsync(cancellationToken, (userId, groupId), (userId, keptGroupId));

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            await bus.Publish(DeletedEvent(userId, groupId), c => c.MessageId = messageId, cancellationToken);
            await bus.Publish(DeletedEvent(userId, groupId), c => c.MessageId = messageId, cancellationToken);
            await bus.Publish(DeletedEvent(userId, Guid.NewGuid()), cancellationToken);

            await WaitForAsync(
                provider,
                (db, ct) => db.GroupTargetAllocations.IgnoreQueryFilters().AnyAsync(a => a.GroupId == groupId, ct),
                stillExists => !stillExists,
                "Deleting the group allocation",
                cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            Assert.Equal([(userId, keptGroupId)], await ReadKeysAsync(cancellationToken));
        }, cancellationToken);
    }

    private static AssetGroupDeleted DeletedEvent(Guid userId, Guid groupId) =>
        new() { UserId = userId, GroupId = groupId, OccurredAtUtc = DateTimeOffset.UtcNow };

    private async Task SeedAsync(CancellationToken cancellationToken, params (Guid UserId, Guid GroupId)[] keys)
    {
        await using var db = OpenDbContext(containers);
        foreach (var (userId, groupId) in keys)
        {
            db.GroupTargetAllocations.Add(new GroupTargetAllocation
            {
                UserId = userId,
                GroupId = groupId,
                UpdatedAt = DateTimeOffset.UtcNow,
                Groups = [new TargetAllocationGroup("Akcje", [AssetClass.Stock], 100m, 5m)],
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<(Guid UserId, Guid GroupId)>> ReadKeysAsync(CancellationToken cancellationToken)
    {
        await using var db = OpenDbContext(containers);
        var rows = await db.GroupTargetAllocations.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        return [.. rows.Select(r => (r.UserId, r.GroupId)).OrderBy(k => k.UserId).ThenBy(k => k.GroupId)];
    }

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(containers, x => x.AddConsumer<AssetGroupDeletedConsumer>(typeof(TestConsumerDefinition)), action, cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<AssetGroupDeletedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
