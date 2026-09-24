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
/// <c>PortfolioArchived</c>/<c>PortfolioRestored</c>/<c>PortfolioDeleted</c> in — spec-03 AC6-7.
/// Archive/restore flips every <see cref="Position.PortfolioIsArchived"/> flag for the portfolio
/// (belt and braces next to spec-02's per-asset <c>AssetPositionChanged</c> fan-out). Delete removes
/// that portfolio's <see cref="Position"/>, <see cref="AssetValuation"/> and <see cref="ValuationSnapshot"/>
/// rows entirely (design decision 1: a deleted portfolio must not keep a ghost value in net worth)
/// without touching any other portfolio. All three consumers share one provider/bus on queue names
/// unique to this test class, mirroring <c>AssetPositionChangedConsumerTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PortfolioLifecycleConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string ArchivedQueueName = "portfolio-archived-consumer-test";
    private const string RestoredQueueName = "portfolio-restored-consumer-test";
    private const string DeletedQueueName = "portfolio-deleted-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_PortfolioArchivedThenRestored_FlipsFlagOnEveryPosition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assetOneId = Guid.NewGuid();
        var assetTwoId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetOneId, userId, portfolioId, cancellationToken, portfolioIsArchived: false);
            await db.SeedPositionAsync(assetTwoId, userId, portfolioId, cancellationToken, portfolioIsArchived: false);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(new PortfolioArchived
            {
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            await WaitForPositionsAsync(provider, portfolioId, cancellationToken, positions => positions.All(p => p.PortfolioIsArchived));

            await bus.Publish(new PortfolioRestored
            {
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var restored = await WaitForPositionsAsync(
                provider, portfolioId, cancellationToken, positions => positions.All(p => !p.PortfolioIsArchived));

            Assert.Equal(2, restored.Count);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_PortfolioDeleted_RemovesPositionsAndValuationHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var deletedPortfolioId = Guid.NewGuid();
        var otherPortfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deletedAssetId = Guid.NewGuid();
        var otherAssetId = Guid.NewGuid();
        var valuationDate = new DateOnly(2026, 8, 1);

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(deletedAssetId, userId, deletedPortfolioId, cancellationToken);
            await db.SeedValuationLineAsync(userId, deletedPortfolioId, deletedAssetId, valuationDate, 1_000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, deletedPortfolioId, valuationDate, 1_000m, cancellationToken);

            await db.SeedPositionAsync(otherAssetId, userId, otherPortfolioId, cancellationToken);
            await db.SeedValuationLineAsync(userId, otherPortfolioId, otherAssetId, valuationDate, 2_000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, otherPortfolioId, valuationDate, 2_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new PortfolioDeleted
            {
                PortfolioId = deletedPortfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            await WaitForNoPositionsAsync(provider, deletedPortfolioId, cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            Assert.Empty(await db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));
            Assert.Empty(await db.AssetValuations.IgnoreQueryFilters().Where(l => l.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));
            Assert.Empty(await db.ValuationSnapshots.IgnoreQueryFilters().Where(s => s.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));

            Assert.Single(await db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == otherPortfolioId).ToListAsync(cancellationToken));
            Assert.Single(await db.AssetValuations.IgnoreQueryFilters().Where(l => l.PortfolioId == otherPortfolioId).ToListAsync(cancellationToken));
            Assert.Single(await db.ValuationSnapshots.IgnoreQueryFilters().Where(s => s.PortfolioId == otherPortfolioId).ToListAsync(cancellationToken));
        }, cancellationToken);
    }

    /// <summary>spec-07 AC9: a restored portfolio gets today's snapshot back right away, recomputed from its positions.</summary>
    [Fact]
    public async Task Restored_RevaluesTodaysSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cashAssetId = Guid.NewGuid();
        var depositAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(cashAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 1_000m, portfolioIsArchived: true);
            await db.SeedPositionAsync(depositAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 500m, portfolioIsArchived: true);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new PortfolioRestored
            {
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken);

            Assert.Equal(1_500m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);
            Assert.False(snapshot.IsStale);

            var lines = await GetLinesAsync(containers, portfolioId, today, cancellationToken);
            Assert.Equal(2, lines.Count);
            Assert.Equal(1_000m, Assert.Single(lines, l => l.AssetId == cashAssetId).ValuePln);
            Assert.Equal(500m, Assert.Single(lines, l => l.AssetId == depositAssetId).ValuePln);
        }, cancellationToken);
    }

    /// <summary>
    /// archived-portfolio-out-of-net-worth AC1: archiving revalues today's snapshot the way removing
    /// the last asset does — the portfolio's only position (1000 PLN of cash) is now archived, so
    /// today's <see cref="ValuationSnapshot"/> drops from 1000 to 0 and today's lines are gone.
    /// </summary>
    [Fact]
    public async Task Consume_PortfolioArchived_SetsTodaysSnapshotToZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cashAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(cashAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 1_000m, portfolioIsArchived: false);
            await db.SeedValuationLineAsync(userId, portfolioId, cashAssetId, today, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedSnapshotAsync(userId, portfolioId, today, 1_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new PortfolioArchived
            {
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken, s => s.TotalPln == 0m);

            Assert.Equal(0m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);
            Assert.Empty(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
        }, cancellationToken);
    }

    /// <summary>
    /// archived-portfolio-out-of-net-worth AC2: the archive revalues today only — yesterday's
    /// snapshot and lines are history and stay exactly as they were. Today's snapshot reaching 0 is
    /// the signal the consume finished, so the "unchanged" assertion cannot pass vacuously.
    /// </summary>
    [Fact]
    public async Task Consume_PortfolioArchived_KeepsEarlierSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var yesterday = today.AddDays(-1);
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cashAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(cashAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 1_000m, portfolioIsArchived: false);
            await db.SeedValuationLineAsync(userId, portfolioId, cashAssetId, yesterday, 1_000m, cancellationToken, quantity: 1_000m);
            await db.SeedSnapshotAsync(userId, portfolioId, yesterday, 1_000m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new PortfolioArchived
            {
                PortfolioId = portfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken, s => s.TotalPln == 0m);

            var earlier = await GetSnapshotAsync(containers, portfolioId, yesterday, cancellationToken);
            Assert.NotNull(earlier);
            Assert.Equal(1_000m, earlier.TotalPln);

            var earlierLine = Assert.Single(await GetLinesAsync(containers, portfolioId, yesterday, cancellationToken));
            Assert.Equal(cashAssetId, earlierLine.AssetId);
            Assert.Equal(1_000m, earlierLine.ValuePln);
        }, cancellationToken);
    }

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<PortfolioArchivedConsumer>(typeof(ArchivedConsumerDefinition));
                x.AddConsumer<PortfolioRestoredConsumer>(typeof(RestoredConsumerDefinition));
                x.AddConsumer<PortfolioDeletedConsumer>(typeof(DeletedConsumerDefinition));
            },
            action,
            cancellationToken);

    private sealed class ArchivedConsumerDefinition : IdempotentConsumerDefinition<PortfolioArchivedConsumer, ReportingDbContext>
    {
        public ArchivedConsumerDefinition() => Endpoint(e => e.Name = ArchivedQueueName);
    }

    private sealed class RestoredConsumerDefinition : IdempotentConsumerDefinition<PortfolioRestoredConsumer, ReportingDbContext>
    {
        public RestoredConsumerDefinition() => Endpoint(e => e.Name = RestoredQueueName);
    }

    private sealed class DeletedConsumerDefinition : IdempotentConsumerDefinition<PortfolioDeletedConsumer, ReportingDbContext>
    {
        public DeletedConsumerDefinition() => Endpoint(e => e.Name = DeletedQueueName);
    }
}
