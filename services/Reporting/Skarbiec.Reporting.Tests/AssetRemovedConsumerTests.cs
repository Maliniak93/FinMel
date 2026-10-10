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
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, earliestLine, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, today.AddDays(-10), 1_000m, cancellationToken, quantity: 1_000m);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(AssetRemovedEvent(assetId, portfolioId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, assetId, cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, portfolioId, cancellationToken);
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
        var portfolioId = Guid.NewGuid();
        var removedAssetId = Guid.NewGuid();
        var keptAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(removedAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedPositionAsync(keptAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 2_000m);

            await db.SeedValuationLineAsync(userId, portfolioId, removedAssetId, yesterday, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, portfolioId, removedAssetId, today, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, portfolioId, keptAssetId, today, 2_000m, cancellationToken, quantity: 2_000m);
            await db.SeedSnapshotAsync(userId, portfolioId, today, 3_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(AssetRemovedEvent(removedAssetId, portfolioId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, removedAssetId, cancellationToken);

            var snapshot = await GetSnapshotAsync(containers, portfolioId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(2_000m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);

            var todaysLine = Assert.Single(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
            Assert.Equal(keptAssetId, todaysLine.AssetId);
            Assert.Equal(2_000m, todaysLine.ValuePln);

            var yesterdaysLine = Assert.Single(await GetLinesAsync(containers, portfolioId, yesterday, cancellationToken));
            Assert.Equal(removedAssetId, yesterdaysLine.AssetId);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_LastAsset_SetsTodaysSnapshotToZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, today, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedSnapshotAsync(userId, portfolioId, today, 1_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(AssetRemovedEvent(assetId, portfolioId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, assetId, cancellationToken);

            var snapshot = await GetSnapshotAsync(containers, portfolioId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(0m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);

            Assert.Empty(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
        }, cancellationToken);
    }

    // PortfolioDeletedConsumer sweeps on its own queue, so a revaluation here could resurrect a zero snapshot after it.
    [Fact]
    public async Task Consume_CascadedFromPortfolio_RemovesPositionWithoutRevaluing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var removedAssetId = Guid.NewGuid();
        var siblingAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(removedAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedPositionAsync(siblingAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 2_000m);

            await db.SeedValuationLineAsync(userId, portfolioId, removedAssetId, today.AddDays(-30), 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, portfolioId, removedAssetId, today, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedValuationLineAsync(userId, portfolioId, siblingAssetId, today, 2_000m, cancellationToken, quantity: 2_000m);
            await db.SeedSnapshotAsync(userId, portfolioId, today, 3_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(AssetRemovedEvent(removedAssetId, portfolioId, userId, cascadedFromPortfolio: true), cancellationToken);

            // Removal and any revaluation commit together, so once the Position is gone the rows are final.
            await WaitForPositionGoneAsync(provider, removedAssetId, cancellationToken);

            var snapshot = await GetSnapshotAsync(containers, portfolioId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(3_000m, snapshot.TotalPln);

            var todaysLines = await GetLinesAsync(containers, portfolioId, today, cancellationToken);
            Assert.Equal(2, todaysLines.Count);
            Assert.Contains(todaysLines, l => l.AssetId == removedAssetId && l.ValuePln == 1_000m);
            Assert.Contains(todaysLines, l => l.AssetId == siblingAssetId && l.ValuePln == 2_000m);

            Assert.Null(await GetRebuildRequestAsync(containers, portfolioId, cancellationToken));
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
