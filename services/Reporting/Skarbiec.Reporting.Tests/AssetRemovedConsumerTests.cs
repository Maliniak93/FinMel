using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

/// <summary>
/// <c>AssetRemoved</c> in, <see cref="Position"/> gone but its historical
/// <see cref="AssetValuation"/> lines kept — spec-03 AC5, design decision 2 ("AssetRemoved keeps the
/// lines": deleting an asset must not silently change what last month's net worth was). Since
/// spec-07 (AC8) only today's line of the removed asset goes, and today's snapshot of its portfolio
/// is recomputed from what is left. Builds its own provider on a queue name unique to this test class.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AssetRemovedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string QueueName = "asset-removed-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_AssetRemoved_DeletesPositionAndKeepsValuationHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var valuationDate = new DateOnly(2026, 8, 1);

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, valuationDate, 1_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Removed(assetId, portfolioId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, assetId, cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var lines = await db.AssetValuations.IgnoreQueryFilters()
                .Where(l => l.AssetId == assetId)
                .ToListAsync(cancellationToken);

            Assert.Single(lines);
        }, cancellationToken);
    }

    /// <summary>spec-07 AC8: the removed asset's line for today goes, the snapshot equals what remains, and earlier history is untouched.</summary>
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
            await bus.Publish(Removed(removedAssetId, portfolioId, userId), cancellationToken);

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

    /// <summary>spec-07 AC8: removing the only asset leaves today's snapshot at zero rather than the stale pre-removal value.</summary>
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
            await bus.Publish(Removed(assetId, portfolioId, userId), cancellationToken);

            await WaitForPositionGoneAsync(provider, assetId, cancellationToken);

            var snapshot = await GetSnapshotAsync(containers, portfolioId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(0m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);

            Assert.Empty(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
        }, cancellationToken);
    }

    private static AssetRemoved Removed(Guid assetId, Guid portfolioId, Guid userId) => new()
    {
        AssetId = assetId,
        PortfolioId = portfolioId,
        UserId = userId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };

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
