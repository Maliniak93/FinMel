using System.Collections.Concurrent;
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

// Builds its own provider, with no HTTP host, on a queue unique to this class.
public sealed class AssetPositionChangedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "asset-position-changed-consumer-test";
    private const string ProbeQueueName = "asset-position-changed-rebuild-probe-test";

    private static readonly ConcurrentBag<Guid> RequestedPortfolios = [];

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_NewAsset_InsertsPositionWithFullState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new AssetPositionChanged
            {
                AssetId = assetId,
                PortfolioId = portfolioId,
                UserId = userId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.Market,
                InstrumentId = instrumentId,
                Currency = "USD",
                Quantity = 12m,
                QuoteUnitsPerQuantity = 1m,
                QuantityHistory = [],
                ManualValueAmount = null,
                ManualValueDate = null,
                PortfolioIsArchived = false,
                IsArchived = false,
                Version = 0,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var position = await WaitForPositionAsync(provider, assetId, cancellationToken);

            Assert.Equal(portfolioId, position.PortfolioId);
            Assert.Equal(userId, position.UserId);
            Assert.Equal(AssetClass.Stock, position.AssetClass);
            Assert.Equal(AssetValuationMode.Market, position.ValuationMode);
            Assert.Equal(instrumentId, position.InstrumentId);
            Assert.Equal("USD", position.Currency);
            Assert.Equal(12m, position.Quantity);
            Assert.Null(position.ManualValueAmount);
            Assert.Null(position.ManualValueDate);
            Assert.False(position.PortfolioIsArchived);
            Assert.Equal(0, position.Version);
            Assert.True(position.UpdatedAt > DateTimeOffset.MinValue);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_EventWithQuoteUnitsPerQuantity_StoresAndUpdatesMultiplier()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 3m, version: 0, quoteUnitsPerQuantity: 31.1034768m), cancellationToken);
            var first = await WaitForPositionAsync(provider, assetId, cancellationToken);
            Assert.Equal(31.1034768m, first.QuoteUnitsPerQuantity);

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 3m, version: 1, quoteUnitsPerQuantity: 100m), cancellationToken);
            var second = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);
            Assert.Equal(100m, second.QuoteUnitsPerQuantity);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SecondEventForSameAsset_UpsertsSingleRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 5m, version: 0), cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken);

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 9m, version: 1), cancellationToken);
            var position = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            Assert.Equal(9m, position.Quantity);
            Assert.Equal(1, position.Version);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var count = await db.Positions.IgnoreQueryFilters().CountAsync(p => p.AssetId == assetId, cancellationToken);
            Assert.Equal(1, count);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_LowerVersionThanStored_IgnoresStaleEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 100m, version: 5), cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 5);
            await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken, s => s.TotalPln == 100m);

            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 1m, version: 4), cancellationToken);

            // Nothing signals an ignored event, so give it time to land before asserting nothing changed.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var position = await db.Positions.IgnoreQueryFilters().SingleAsync(p => p.AssetId == assetId, cancellationToken);

            Assert.Equal(5, position.Version);
            Assert.Equal(100m, position.Quantity);

            var snapshot = await GetSnapshotAsync(containers, portfolioId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(100m, snapshot.TotalPln);

            var line = Assert.Single(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
            Assert.Equal(100m, line.Quantity);
            Assert.Equal(100m, line.ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdTwice_AppliesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = Event(assetId, portfolioId, userId, quantity: 42m, version: 0);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken);

            // Same MessageId: the inbox must skip the consumer body.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var positions = await db.Positions.IgnoreQueryFilters()
                .Where(p => p.AssetId == assetId)
                .ToListAsync(cancellationToken);

            var position = Assert.Single(positions);
            Assert.Equal(42m, position.Quantity);

            var snapshots = await db.ValuationSnapshots.IgnoreQueryFilters()
                .Where(s => s.PortfolioId == portfolioId && s.Date == today)
                .ToListAsync(cancellationToken);
            Assert.Equal(42m, Assert.Single(snapshots).TotalPln);

            var lines = await db.AssetValuations.IgnoreQueryFilters()
                .Where(l => l.AssetId == assetId && l.Date == today)
                .ToListAsync(cancellationToken);
            Assert.Equal(42m, Assert.Single(lines).ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_PlnCash_WritesTodaysLineAndSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 1_000m, version: 0), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken);

            Assert.Equal(1_000m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);
            Assert.False(snapshot.IsStale);

            var line = Assert.Single(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
            Assert.Equal(assetId, line.AssetId);
            Assert.Equal(userId, line.UserId);
            Assert.Equal(AssetClass.Cash, line.AssetClass);
            Assert.Equal(1_000m, line.ValuePln);
            Assert.False(line.IsStale);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_ForeignCash_ValuesWithLatestLocalFxRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedLatestFxRateAsync("USDPLN", today, 4.00m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetId, portfolioId, userId, quantity: 100m, version: 0, currency: "USD"), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken);

            var line = Assert.Single(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
            Assert.Equal(400m, line.ValuePln);
            Assert.Equal(4.00m, line.FxRateUsed);
            Assert.False(line.IsStale);
            Assert.Equal(400m, snapshot.TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_InstrumentWithoutLocalPrice_WritesZeroStaleLine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new AssetPositionChanged
            {
                AssetId = assetId,
                PortfolioId = portfolioId,
                UserId = userId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.Market,
                InstrumentId = Guid.NewGuid(),
                Currency = "PLN",
                Quantity = 10m,
                QuoteUnitsPerQuantity = 1m,
                QuantityHistory = [],
                PortfolioIsArchived = false,
                IsArchived = false,
                Version = 0,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken);

            var line = Assert.Single(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
            Assert.Equal(assetId, line.AssetId);
            Assert.Equal(0m, line.ValuePln);
            Assert.True(line.IsStale);

            Assert.Equal(0m, snapshot.TotalPln);
            Assert.True(snapshot.IsStale);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_RevaluesOnlyTheEventsPortfolio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();

        var portfolioAId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var portfolioBId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetAId, userId, portfolioAId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m);
            await db.SeedValuationLineAsync(userId, portfolioAId, assetAId, today, 100m, cancellationToken, quantity: 100m);
            await db.SeedSnapshotAsync(userId, portfolioAId, today, 100m, cancellationToken);

            // B's position says 5 000 PLN but its rows for today say 777, so any revaluation of B would show as 5 000.
            await db.SeedPositionAsync(assetBId, userId, portfolioBId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 5_000m);
            await db.SeedValuationLineAsync(userId, portfolioBId, assetBId, today, 777m, cancellationToken, quantity: 777m);
            await db.SeedSnapshotAsync(userId, portfolioBId, today, 777m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetAId, portfolioAId, userId, quantity: 300m, version: 1), cancellationToken);

            await WaitForSnapshotAsync(provider, portfolioAId, today, cancellationToken, s => s.TotalPln == 300m);

            var snapshotB = await GetSnapshotAsync(containers, portfolioBId, today, cancellationToken);
            Assert.NotNull(snapshotB);
            Assert.Equal(777m, snapshotB.TotalPln);

            var lineB = Assert.Single(await GetLinesAsync(containers, portfolioBId, today, cancellationToken));
            Assert.Equal(777m, lineB.ValuePln);
            Assert.Equal(777m, lineB.Quantity);
        }, cancellationToken);
    }

    // The non-archived portfolio consumed afterwards is the positive control: the revaluation path is live.
    [Fact]
    public async Task Consume_ArchivedPortfolio_DoesNotRevalue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();

        var archivedPortfolioId = Guid.NewGuid();
        var archivedAssetId = Guid.NewGuid();
        var controlPortfolioId = Guid.NewGuid();
        var controlAssetId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(
                Event(archivedAssetId, archivedPortfolioId, userId, quantity: 1_000m, version: 0, portfolioIsArchived: true),
                cancellationToken);

            // The upsert and any revaluation commit together, so a visible position means the consume has finished.
            await WaitForPositionAsync(provider, archivedAssetId, cancellationToken);

            await bus.Publish(Event(controlAssetId, controlPortfolioId, userId, quantity: 50m, version: 0), cancellationToken);
            await WaitForSnapshotAsync(provider, controlPortfolioId, today, cancellationToken);

            Assert.Null(await GetSnapshotAsync(containers, archivedPortfolioId, today, cancellationToken));
            Assert.Empty(await GetLinesAsync(containers, archivedPortfolioId, today, cancellationToken));
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_ArchivedAsset_DropsOutOfToday()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var yesterday = today.AddDays(-1);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedValuationLineAsync(userId, portfolioId, assetAId, yesterday, 111m, cancellationToken, quantity: 111m);
            await db.SeedValuationLineAsync(userId, portfolioId, assetBId, yesterday, 222m, cancellationToken, quantity: 222m);
            await db.SeedSnapshotAsync(userId, portfolioId, yesterday, 333m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetAId, portfolioId, userId, quantity: 100m, version: 0), cancellationToken);
            await bus.Publish(Event(assetBId, portfolioId, userId, quantity: 200m, version: 0), cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken, s => s.TotalPln == 300m);

            await bus.Publish(Event(assetAId, portfolioId, userId, quantity: 100m, version: 1, isArchived: true), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken, s => s.TotalPln == 200m);
            Assert.Equal(200m, snapshot.TotalPln);
            var line = Assert.Single(await GetLinesAsync(containers, portfolioId, today, cancellationToken));
            Assert.Equal(assetBId, line.AssetId);
            Assert.True((await WaitForPositionAsync(provider, assetAId, cancellationToken)).IsArchived);

            var yesterdaySnapshot = await GetSnapshotAsync(containers, portfolioId, yesterday, cancellationToken);
            Assert.Equal(333m, yesterdaySnapshot!.TotalPln);
            Assert.Equal(2, (await GetLinesAsync(containers, portfolioId, yesterday, cancellationToken)).Count);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_RestoredAsset_RejoinsToday()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetAId, portfolioId, userId, quantity: 100m, version: 0), cancellationToken);
            await bus.Publish(Event(assetBId, portfolioId, userId, quantity: 200m, version: 0), cancellationToken);
            await bus.Publish(Event(assetAId, portfolioId, userId, quantity: 100m, version: 1, isArchived: true), cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken, s => s.TotalPln == 200m);

            await bus.Publish(Event(assetAId, portfolioId, userId, quantity: 100m, version: 2, isArchived: false), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, today, cancellationToken, s => s.TotalPln == 300m);
            Assert.Equal(300m, snapshot.TotalPln);
            var lines = await GetLinesAsync(containers, portfolioId, today, cancellationToken);
            Assert.Equal(2, lines.Count);
            Assert.Contains(lines, l => l.AssetId == assetAId && l.ValuePln == 100m);
            Assert.False((await WaitForPositionAsync(provider, assetAId, cancellationToken)).IsArchived);
        }, cancellationToken);
    }

    [Fact]
    public async Task PastChange_RequestsRebuildFromEarliestChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, portfolioId, userId, quantity: 15m, version: 1,
                    history: [(today.AddDays(-30), 10m), (today.AddDays(-10), 15m)]),
                cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, portfolioId, cancellationToken);
            Assert.Equal(today.AddDays(-10), request.FromDate);
            Assert.Equal(userId, request.UserId);

            await WaitForAsync(
                provider,
                (_, _) => Task.FromResult(RequestedPortfolios.Count(id => id == portfolioId)),
                count => count >= 1,
                "The PortfolioHistoryRebuildRequested message",
                cancellationToken);

            // Nothing signals a duplicate publish, so give one time to land before asserting there is exactly one.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            Assert.Equal(1, RequestedPortfolios.Count(id => id == portfolioId));
        }, cancellationToken);
    }

    [Fact]
    public async Task PendingRequest_KeepsEarliestFrom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-20), cancellationToken, revision: 3);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, portfolioId, userId, quantity: 15m, version: 1,
                    history: [(today.AddDays(-30), 10m), (today.AddDays(-5), 15m)]),
                cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, portfolioId, cancellationToken, r => r.Revision > 3);

            Assert.Equal(today.AddDays(-20), request.FromDate);
            Assert.Equal(4, request.Revision);
        }, cancellationToken);
    }

    [Fact]
    public async Task TodayOnlyOrNoChange_RequestsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(
                Event(assetId, portfolioId, userId, quantity: 12m, version: 1,
                    history: [(today.AddDays(-30), 10m), (today, 12m)]),
                cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            await bus.Publish(
                Event(assetId, portfolioId, userId, quantity: 12m, version: 2,
                    history: [(today.AddDays(-30), 10m), (today, 12m)]),
                cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 2);

            // Nothing signals an absent request, so give a late outbox delivery time to land before asserting none.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Null(await GetRebuildRequestAsync(containers, portfolioId, cancellationToken));
            Assert.DoesNotContain(portfolioId, RequestedPortfolios);
        }, cancellationToken);
    }

    [Fact]
    public async Task AssetRestored_ClearsArchivedOnAndRequestsRebuildFromIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                isArchived: true, archivedOn: today.AddDays(-5), quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, portfolioId, userId, quantity: 10m, version: 1, isArchived: false, history: [(today.AddDays(-30), 10m)]),
                cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, portfolioId, cancellationToken);
            var position = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            Assert.Equal(today.AddDays(-5), request.FromDate);
            Assert.Null(position.ArchivedOn);
        }, cancellationToken);
    }

    [Fact]
    public async Task AssetArchivedToday_StampsArchivedOnFromEventDateAndRequestsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, portfolioId, userId, quantity: 10m, version: 1, isArchived: true, history: [(today.AddDays(-30), 10m)]),
                cancellationToken);

            var position = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            Assert.Equal(today, position.ArchivedOn);
            Assert.Null(await GetRebuildRequestAsync(containers, portfolioId, cancellationToken));
        }, cancellationToken);
    }

    private static AssetPositionChanged Event(
        Guid assetId,
        Guid portfolioId,
        Guid userId,
        decimal quantity,
        long version,
        string currency = "PLN",
        bool portfolioIsArchived = false,
        bool isArchived = false,
        decimal quoteUnitsPerQuantity = 1m,
        IReadOnlyList<(DateOnly Date, decimal Quantity)>? history = null) => new()
        {
            QuoteUnitsPerQuantity = quoteUnitsPerQuantity,
            QuantityHistory = [.. (history ?? []).Select(p => new QuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            IsArchived = isArchived,
            AssetId = assetId,
            PortfolioId = portfolioId,
            UserId = userId,
            AssetClass = AssetClass.Cash,
            ValuationMode = AssetValuationMode.CurrencyValued,
            Currency = currency,
            Quantity = quantity,
            PortfolioIsArchived = portfolioIsArchived,
            Version = version,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };

    private Task RunConsumerAsync(Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<AssetPositionChangedConsumer>(typeof(TestConsumerDefinition));
                x.AddConsumer<RebuildProbeConsumer>(typeof(RebuildProbeDefinition));
            },
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }

    private sealed class RebuildProbeConsumer : IConsumer<PortfolioHistoryRebuildRequested>
    {
        public Task Consume(ConsumeContext<PortfolioHistoryRebuildRequested> context)
        {
            RequestedPortfolios.Add(context.Message.PortfolioId);
            return Task.CompletedTask;
        }
    }

    private sealed class RebuildProbeDefinition : ConsumerDefinition<RebuildProbeConsumer>
    {
        public RebuildProbeDefinition() => Endpoint(e => e.Name = ProbeQueueName);
    }
}
