using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// All three consumers share one provider on queue names unique to this class.
public sealed class PortfolioLifecycleConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
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
    public async Task Delete_RequestsUserRebuild()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var earliestLine = today.AddDays(-10);
        var deletedPortfolioId = Guid.NewGuid();
        var otherPortfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deletedAssetId = Guid.NewGuid();
        var otherAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(deletedAssetId, userId, deletedPortfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedPositionAsync(otherAssetId, userId, otherPortfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 2_000m);

            foreach (var date in new[] { earliestLine, today.AddDays(-3), today })
            {
                await db.SeedValuationLineAsync(userId, deletedPortfolioId, deletedAssetId, date, 1_000m, cancellationToken, quantity: 1_000m);
                await db.SeedValuationLineAsync(userId, otherPortfolioId, otherAssetId, date, 2_000m, cancellationToken, quantity: 2_000m);
                await db.SeedSnapshotAsync(userId, date, 3_000m, cancellationToken);
            }
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
            var request = await WaitForRebuildRequestAsync(provider, userId, cancellationToken);

            Assert.Equal(earliestLine, request.FromDate);
            Assert.Equal(userId, request.UserId);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            Assert.Empty(await db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));
            Assert.Empty(await db.AssetValuations.IgnoreQueryFilters().Where(l => l.PortfolioId == deletedPortfolioId).ToListAsync(cancellationToken));

            Assert.Single(await db.Positions.IgnoreQueryFilters().Where(p => p.PortfolioId == otherPortfolioId).ToListAsync(cancellationToken));
            Assert.Equal(3, await db.AssetValuations.IgnoreQueryFilters().CountAsync(l => l.PortfolioId == otherPortfolioId, cancellationToken));

            var todaySnapshot = await GetSnapshotAsync(containers, userId, today, cancellationToken);
            Assert.NotNull(todaySnapshot);
            Assert.Equal(2_000m, todaySnapshot.TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Archive_DropsFromUserSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var yesterday = today.AddDays(-1);
        var archivedPortfolioId = Guid.NewGuid();
        var keptPortfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var archivedAssetId = Guid.NewGuid();
        var keptAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(archivedAssetId, userId, archivedPortfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 1_000m);
            await db.SeedPositionAsync(keptAssetId, userId, keptPortfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 500m);

            foreach (var date in new[] { yesterday, today })
            {
                await db.SeedValuationLineAsync(userId, archivedPortfolioId, archivedAssetId, date, 1_000m, cancellationToken, quantity: 1_000m);
                await db.SeedValuationLineAsync(userId, keptPortfolioId, keptAssetId, date, 500m, cancellationToken, quantity: 500m);
                await db.SeedSnapshotAsync(userId, date, 1_500m, cancellationToken);
            }
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new PortfolioArchived
            {
                PortfolioId = archivedPortfolioId,
                UserId = userId,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 500m);
            Assert.Equal(userId, snapshot.UserId);

            var todaysLine = Assert.Single(await GetUserLinesAsync(containers, userId, today, cancellationToken));
            Assert.Equal(keptAssetId, todaysLine.AssetId);

            var earlier = await GetSnapshotAsync(containers, userId, yesterday, cancellationToken);
            Assert.NotNull(earlier);
            Assert.Equal(1_500m, earlier.TotalPln);
            Assert.Equal(2, (await GetUserLinesAsync(containers, userId, yesterday, cancellationToken)).Count);
        }, cancellationToken);
    }

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

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken);

            Assert.Equal(1_500m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);
            Assert.False(snapshot.IsStale);

            var lines = await GetLinesAsync(containers, portfolioId, today, cancellationToken);
            Assert.Equal(2, lines.Count);
            Assert.Equal(1_000m, Assert.Single(lines, l => l.AssetId == cashAssetId).ValuePln);
            Assert.Equal(500m, Assert.Single(lines, l => l.AssetId == depositAssetId).ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Restore_RequestsRebuildFromArchiveDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 1_000m, portfolioIsArchived: true,
                portfolioArchivedOn: today.AddDays(-5), quantityHistory: [(today.AddDays(-20), 1_000m)]);
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

            var request = await WaitForRebuildRequestAsync(provider, userId, cancellationToken);
            var positions = await WaitForPositionsAsync(provider, portfolioId, cancellationToken, ps => ps.All(p => !p.PortfolioIsArchived));

            Assert.Equal(today.AddDays(-5), request.FromDate);
            Assert.Equal(userId, request.UserId);
            Assert.All(positions, p => Assert.Null(p.PortfolioArchivedOn));
        }, cancellationToken);
    }

    [Fact]
    public async Task Archive_StampsPortfolioArchivedOnFromEventDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 1_000m, quantityHistory: [(today.AddDays(-20), 1_000m)]);
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

            var positions = await WaitForPositionsAsync(provider, portfolioId, cancellationToken, ps => ps.All(p => p.PortfolioIsArchived));

            Assert.All(positions, p => Assert.Equal(today, p.PortfolioArchivedOn));
        }, cancellationToken);
    }

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
            await db.SeedSnapshotAsync(userId, today, 1_000m, cancellationToken);
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

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 0m);

            Assert.Equal(0m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);
            Assert.Empty(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
        }, cancellationToken);
    }

    // Today's snapshot reaching 0 signals the consume finished, so the unchanged assertion cannot pass vacuously.
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
            await db.SeedSnapshotAsync(userId, yesterday, 1_000m, cancellationToken);
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

            await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 0m);

            var earlier = await GetSnapshotAsync(containers, userId, yesterday, cancellationToken);
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
