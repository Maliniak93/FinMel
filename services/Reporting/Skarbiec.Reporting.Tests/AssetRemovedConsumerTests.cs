using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.PositionEvents;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// Builds its own provider, with no HTTP host, on a queue unique to this class.
public sealed class AssetRemovedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "asset-removed-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_RequestsRebuildFromEarliestLine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var earliestLine = today.AddDays(-30);
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, accountId, assetId, earliestLine, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, accountId, assetId, today.AddDays(-10), 1_000m, cancellationToken, quantity: 1_000m);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(AssetRemovedEvent(assetId, accountId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, assetId, cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, userId, cancellationToken);
            Assert.Equal(earliestLine, request.FromDate);
            Assert.Equal(userId, request.UserId);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_RemovesTodaysLineAndRecomputesSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var yesterday = today.AddDays(-1);
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var removedAssetId = Guid.NewGuid();
        var keptAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(removedAssetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedPositionAsync(keptAssetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 2_000m);

            await db.SeedValuationLineAsync(userId, accountId, removedAssetId, yesterday, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, accountId, removedAssetId, today, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, accountId, keptAssetId, today, 2_000m, cancellationToken, quantity: 2_000m);
            await db.SeedSnapshotAsync(userId, today, 3_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(AssetRemovedEvent(removedAssetId, accountId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, removedAssetId, cancellationToken);

            var snapshot = await GetSnapshotAsync(containers, userId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(2_000m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);

            var todaysLine = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
            Assert.Equal(keptAssetId, todaysLine.AssetId);
            Assert.Equal(2_000m, todaysLine.ValuePln);

            var yesterdaysLine = Assert.Single(await GetLinesAsync(containers, accountId, yesterday, cancellationToken));
            Assert.Equal(removedAssetId, yesterdaysLine.AssetId);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_LastAsset_SetsTodaysSnapshotToZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, accountId, assetId, today, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedSnapshotAsync(userId, today, 1_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(AssetRemovedEvent(assetId, accountId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, assetId, cancellationToken);

            var snapshot = await GetSnapshotAsync(containers, userId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(0m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);

            Assert.Empty(await GetLinesAsync(containers, accountId, today, cancellationToken));
        }, cancellationToken);
    }

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x => x.AddConsumer<AssetRemovedConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<AssetRemovedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
