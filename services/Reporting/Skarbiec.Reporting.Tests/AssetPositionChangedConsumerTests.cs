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

    private static readonly ConcurrentBag<Guid> RequestedUsers = [];

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_NewAsset_InsertsPositionWithFullState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new AssetPositionChanged
            {
                AssetId = assetId,
                AccountId = accountId,
                UserId = userId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.Market,
                InstrumentId = instrumentId,
                Currency = "USD",
                Quantity = 12m,
                QuoteUnitsPerQuantity = 1m,
                ClassHistory = [],
                QuantityHistory = [],
                IsArchived = false,
                Version = 0,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var position = await WaitForPositionAsync(provider, assetId, cancellationToken);

            Assert.Equal(accountId, position.AccountId);
            Assert.Equal(userId, position.UserId);
            Assert.Equal(AssetClass.Stock, position.AssetClass);
            Assert.Equal(AssetValuationMode.Market, position.ValuationMode);
            Assert.Equal(instrumentId, position.InstrumentId);
            Assert.Equal("USD", position.Currency);
            Assert.Equal(12m, position.Quantity);
            Assert.Equal(0, position.Version);
            Assert.True(position.UpdatedAt > DateTimeOffset.MinValue);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_EventWithQuoteUnitsPerQuantity_StoresAndUpdatesMultiplier()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, accountId, userId, quantity: 3m, version: 0, quoteUnitsPerQuantity: 31.1034768m), cancellationToken);
            var first = await WaitForPositionAsync(provider, assetId, cancellationToken);
            Assert.Equal(31.1034768m, first.QuoteUnitsPerQuantity);

            await bus.Publish(Event(assetId, accountId, userId, quantity: 3m, version: 1, quoteUnitsPerQuantity: 100m), cancellationToken);
            var second = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);
            Assert.Equal(100m, second.QuoteUnitsPerQuantity);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SecondEventForSameAsset_UpsertsSingleRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, accountId, userId, quantity: 5m, version: 0), cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken);

            await bus.Publish(Event(assetId, accountId, userId, quantity: 9m, version: 1), cancellationToken);
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
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, accountId, userId, quantity: 100m, version: 5), cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 5);
            await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 100m);

            await bus.Publish(Event(assetId, accountId, userId, quantity: 1m, version: 4), cancellationToken);

            // Nothing signals an ignored event, so give it time to land before asserting nothing changed.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var position = await db.Positions.IgnoreQueryFilters().SingleAsync(p => p.AssetId == assetId, cancellationToken);

            Assert.Equal(5, position.Version);
            Assert.Equal(100m, position.Quantity);

            var snapshot = await GetSnapshotAsync(containers, userId, today, cancellationToken);
            Assert.NotNull(snapshot);
            Assert.Equal(100m, snapshot.TotalPln);

            var line = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
            Assert.Equal(100m, line.Quantity);
            Assert.Equal(100m, line.ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdTwice_AppliesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = Event(assetId, accountId, userId, quantity: 42m, version: 0);

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken);
            await WaitForSnapshotAsync(provider, userId, today, cancellationToken);

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
                .Where(s => s.UserId == userId && s.Date == today)
                .ToListAsync(cancellationToken);
            Assert.Equal(42m, Assert.Single(snapshots).TotalPln);

            var lines = await db.AssetValuations.IgnoreQueryFilters()
                .Where(l => l.AssetId == assetId && l.Date == today)
                .ToListAsync(cancellationToken);
            Assert.Equal(42m, Assert.Single(lines).ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_PublishesLatestValuationChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var stockAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(stockAssetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 80_000m);
            await db.SeedPositionAsync(Guid.NewGuid(), userId, accountId, cancellationToken,
                assetClass: AssetClass.Etf, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 30_000m);
            await db.SeedPositionAsync(Guid.NewGuid(), userId, accountId, cancellationToken,
                assetClass: AssetClass.PreciousMetal, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 12_000m);
        }

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(new AssetPositionChanged
            {
                AssetId = stockAssetId,
                AccountId = accountId,
                UserId = userId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.CurrencyValued,
                Currency = "PLN",
                Quantity = 80_000m,
                QuoteUnitsPerQuantity = 1m,
                ClassHistory = [],
                QuantityHistory = [],
                IsArchived = false,
                Version = 1,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var published = Assert.Single(await provider.GetRequiredService<LatestValuationRecorder>()
                .SettledDeliveriesForAsync(userId, cancellationToken));

            Assert.Equal(today, published.Date);
            Assert.Equal(122_000m, published.TotalPln);
            Assert.Equal(
                new Dictionary<AssetClass, decimal>
                {
                    [AssetClass.Stock] = 80_000m,
                    [AssetClass.Etf] = 30_000m,
                    [AssetClass.PreciousMetal] = 12_000m,
                },
                published.ByAssetClass.ToDictionary(c => c.AssetClass, c => c.ValuePln));
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_PlnCash_WritesTodaysLineAndSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetId, accountId, userId, quantity: 1_000m, version: 0), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken);

            Assert.Equal(1_000m, snapshot.TotalPln);
            Assert.Equal(userId, snapshot.UserId);
            Assert.False(snapshot.IsStale);

            var line = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
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
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedLatestFxRateAsync("USDPLN", today, 4.00m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetId, accountId, userId, quantity: 100m, version: 0, currency: "USD"), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken);

            var line = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
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
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var today = Today;

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new AssetPositionChanged
            {
                AssetId = assetId,
                AccountId = accountId,
                UserId = userId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.Market,
                InstrumentId = Guid.NewGuid(),
                Currency = "PLN",
                Quantity = 10m,
                QuoteUnitsPerQuantity = 1m,
                ClassHistory = [],
                QuantityHistory = [],
                IsArchived = false,
                Version = 0,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken);

            var line = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
            Assert.Equal(assetId, line.AssetId);
            Assert.Equal(0m, line.ValuePln);
            Assert.True(line.IsStale);

            Assert.Equal(0m, snapshot.TotalPln);
            Assert.True(snapshot.IsStale);
        }, cancellationToken);
    }

    [Fact]
    public async Task TwoAccounts_OneUserSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();

        var accountAId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var accountBId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetAId, userId, accountAId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m);
            await db.SeedPositionAsync(assetBId, userId, accountBId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 5_000m);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetAId, accountAId, userId, quantity: 300m, version: 1), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 5_300m);
            Assert.Equal(userId, snapshot.UserId);

            await using var db = OpenDbContext(containers);
            Assert.Equal(1, await db.ValuationSnapshots.IgnoreQueryFilters().CountAsync(s => s.UserId == userId && s.Date == today, cancellationToken));

            var lines = await GetUserLinesAsync(containers, userId, today, cancellationToken);
            Assert.Equal(2, lines.Count);
            Assert.Equal(5_300m, lines.Sum(l => l.ValuePln));
            Assert.Equal(300m, lines.Single(l => l.AssetId == assetAId).ValuePln);
            Assert.Equal(accountAId, lines.Single(l => l.AssetId == assetAId).AccountId);
            Assert.Equal(5_000m, lines.Single(l => l.AssetId == assetBId).ValuePln);
            Assert.Equal(accountBId, lines.Single(l => l.AssetId == assetBId).AccountId);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_ArchivedAsset_DropsOutOfToday()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var yesterday = today.AddDays(-1);
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedValuationLineAsync(userId, accountId, assetAId, yesterday, 111m, cancellationToken, quantity: 111m);
            await db.SeedValuationLineAsync(userId, accountId, assetBId, yesterday, 222m, cancellationToken, quantity: 222m);
            await db.SeedSnapshotAsync(userId, yesterday, 333m, cancellationToken);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetAId, accountId, userId, quantity: 100m, version: 0), cancellationToken);
            await bus.Publish(Event(assetBId, accountId, userId, quantity: 200m, version: 0), cancellationToken);
            await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 300m);

            await bus.Publish(Event(assetAId, accountId, userId, quantity: 100m, version: 1, isArchived: true), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 200m);
            Assert.Equal(200m, snapshot.TotalPln);
            var line = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
            Assert.Equal(assetBId, line.AssetId);
            Assert.True((await WaitForPositionAsync(provider, assetAId, cancellationToken)).IsArchived);

            var yesterdaySnapshot = await GetSnapshotAsync(containers, userId, yesterday, cancellationToken);
            Assert.Equal(333m, yesterdaySnapshot!.TotalPln);
            Assert.Equal(2, (await GetLinesAsync(containers, accountId, yesterday, cancellationToken)).Count);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_RestoredAsset_RejoinsToday()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(Event(assetAId, accountId, userId, quantity: 100m, version: 0), cancellationToken);
            await bus.Publish(Event(assetBId, accountId, userId, quantity: 200m, version: 0), cancellationToken);
            await bus.Publish(Event(assetAId, accountId, userId, quantity: 100m, version: 1, isArchived: true), cancellationToken);
            await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 200m);

            await bus.Publish(Event(assetAId, accountId, userId, quantity: 100m, version: 2, isArchived: false), cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, userId, today, cancellationToken, s => s.TotalPln == 300m);
            Assert.Equal(300m, snapshot.TotalPln);
            var lines = await GetLinesAsync(containers, accountId, today, cancellationToken);
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
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 15m, version: 1,
                    history: [(today.AddDays(-30), 10m), (today.AddDays(-10), 15m)]),
                cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, userId, cancellationToken);
            Assert.Equal(today.AddDays(-10), request.FromDate);
            Assert.Equal(userId, request.UserId);

            await WaitForAsync(
                provider,
                (_, _) => Task.FromResult(RequestedUsers.Count(id => id == userId)),
                count => count >= 1,
                "The HistoryRebuildRequested message",
                cancellationToken);

            // Nothing signals a duplicate publish, so give one time to land before asserting there is exactly one.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            Assert.Equal(1, RequestedUsers.Count(id => id == userId));
        }, cancellationToken);
    }

    [Fact]
    public async Task PendingRequest_KeepsEarliestFrom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-20), cancellationToken, revision: 3);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 15m, version: 1,
                    history: [(today.AddDays(-30), 10m), (today.AddDays(-5), 15m)]),
                cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, userId, cancellationToken, r => r.Revision > 3);

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
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 12m, version: 1,
                    history: [(today.AddDays(-30), 10m), (today, 12m)]),
                cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 12m, version: 2,
                    history: [(today.AddDays(-30), 10m), (today, 12m)]),
                cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 2);

            // Nothing signals an absent request, so give a late outbox delivery time to land before asserting none.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Null(await GetRebuildRequestAsync(containers, userId, cancellationToken));
            Assert.DoesNotContain(userId, RequestedUsers);
        }, cancellationToken);
    }

    [Fact]
    public async Task AssetRestored_ClearsArchivedOnAndRequestsRebuildFromIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                isArchived: true, archivedOn: today.AddDays(-5), quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 1, isArchived: false, history: [(today.AddDays(-30), 10m)]),
                cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, userId, cancellationToken);
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
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: [(today.AddDays(-30), 10m)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 1, isArchived: true, history: [(today.AddDays(-30), 10m)]),
                cancellationToken);

            var position = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            Assert.Equal(today, position.ArchivedOn);
            Assert.Null(await GetRebuildRequestAsync(containers, userId, cancellationToken));
        }, cancellationToken);
    }

    [Fact]
    public async Task StoresClassHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var history = new[] { (today.AddDays(-10), 10m) };

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 0, history: history, assetClass: AssetClass.Etf,
                    classHistory: [(today.AddDays(-10), AssetClass.Cash), (today.AddDays(-3), AssetClass.Etf)]),
                cancellationToken);
            var stored = await WaitForPositionAsync(provider, assetId, cancellationToken);

            Assert.Equal(AssetClass.Etf, stored.AssetClass);
            Assert.Equal(
                [(today.AddDays(-10), AssetClass.Cash), (today.AddDays(-3), AssetClass.Etf)],
                stored.ClassHistory.Select(p => (p.Date, p.AssetClass)));

            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 1, history: history, assetClass: AssetClass.Stock,
                    classHistory: [(today.AddDays(-10), AssetClass.Cash), (today.AddDays(-6), AssetClass.Stock)]),
                cancellationToken);
            var overwritten = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            Assert.Equal(AssetClass.Stock, overwritten.AssetClass);
            Assert.Equal(
                [(today.AddDays(-10), AssetClass.Cash), (today.AddDays(-6), AssetClass.Stock)],
                overwritten.ClassHistory.Select(p => (p.Date, p.AssetClass)));
        }, cancellationToken);
    }

    [Fact]
    public async Task ClassTimelineChange_RequestsRebuildFromFirstDifference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var history = new[] { (today.AddDays(-30), 10m) };

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: history,
                classHistory: [(today.AddDays(-30), AssetClass.Cash), (today.AddDays(-10), AssetClass.Stock)]);
        }

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 1, history: history, assetClass: AssetClass.Stock,
                    classHistory: [(today.AddDays(-30), AssetClass.Cash), (today.AddDays(-10), AssetClass.Stock)]),
                cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            await bus.Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 2, history: history, assetClass: AssetClass.Stock,
                    classHistory: [(today.AddDays(-30), AssetClass.Cash), (today.AddDays(-7), AssetClass.Stock)]),
                cancellationToken);

            var request = await WaitForRebuildRequestAsync(provider, userId, cancellationToken);
            Assert.Equal(today.AddDays(-10), request.FromDate);
            Assert.Equal(userId, request.UserId);
        }, cancellationToken);
    }

    [Fact]
    public async Task TodayOnlyClassChange_NoRebuild()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var history = new[] { (today.AddDays(-30), 10m) };

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: history, classHistory: [(today.AddDays(-30), AssetClass.Cash)]);
        }

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 1, history: history, assetClass: AssetClass.Etf,
                    classHistory: [(today.AddDays(-30), AssetClass.Cash), (today, AssetClass.Etf)]),
                cancellationToken);
            await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);

            var line = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
            Assert.Equal(AssetClass.Etf, line.AssetClass);

            // Nothing signals an absent request, so give a late outbox delivery time to land before asserting none.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Null(await GetRebuildRequestAsync(containers, userId, cancellationToken));
            Assert.DoesNotContain(userId, RequestedUsers);
        }, cancellationToken);
    }

    [Fact]
    public async Task StoresGroupId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();

        await RunConsumerAsync(async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(Event(assetId, accountId, userId, quantity: 5m, version: 0, groupId: groupA), cancellationToken);
            var first = await WaitForPositionAsync(provider, assetId, cancellationToken);
            Assert.Equal(groupA, first.GroupId);

            await bus.Publish(Event(assetId, accountId, userId, quantity: 5m, version: 1, groupId: groupB), cancellationToken);
            var second = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);
            Assert.Equal(groupB, second.GroupId);

            await bus.Publish(Event(assetId, accountId, userId, quantity: 5m, version: 2, groupId: null), cancellationToken);
            var third = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 2);
            Assert.Null(third.GroupId);
        }, cancellationToken);
    }

    [Fact]
    public async Task GroupOnlyChange_NoRebuild()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var assetId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var history = new[] { (today.AddDays(-30), 10m) };

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, quantity: 10m,
                quantityHistory: history, classHistory: [(today.AddDays(-30), AssetClass.Cash)]);
        }

        await RunConsumerAsync(async provider =>
        {
            await provider.GetRequiredService<IBus>().Publish(
                Event(assetId, accountId, userId, quantity: 10m, version: 1, history: history,
                    classHistory: [(today.AddDays(-30), AssetClass.Cash)], groupId: groupId),
                cancellationToken);
            var position = await WaitForPositionAsync(provider, assetId, cancellationToken, p => p.Version == 1);
            Assert.Equal(groupId, position.GroupId);

            var line = Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken));
            Assert.Equal(10m, line.ValuePln);

            // Nothing signals an absent request, so give a late outbox delivery time to land before asserting none.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Null(await GetRebuildRequestAsync(containers, userId, cancellationToken));
            Assert.DoesNotContain(userId, RequestedUsers);
        }, cancellationToken);
    }

    private static AssetPositionChanged Event(
        Guid assetId,
        Guid accountId,
        Guid userId,
        decimal quantity,
        long version,
        string currency = "PLN",
        bool isArchived = false,
        decimal quoteUnitsPerQuantity = 1m,
        IReadOnlyList<(DateOnly Date, decimal Quantity)>? history = null,
        AssetClass assetClass = AssetClass.Cash,
        IReadOnlyList<(DateOnly Date, AssetClass AssetClass)>? classHistory = null,
        Guid? groupId = null) => new()
        {
            GroupId = groupId,
            QuoteUnitsPerQuantity = quoteUnitsPerQuantity,
            ClassHistory = [.. (classHistory ?? []).Select(p => new ClassPoint { Date = p.Date, AssetClass = p.AssetClass })],
            QuantityHistory = [.. (history ?? []).Select(p => new QuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            IsArchived = isArchived,
            AssetId = assetId,
            AccountId = accountId,
            UserId = userId,
            AssetClass = assetClass,
            ValuationMode = AssetValuationMode.CurrencyValued,
            Currency = currency,
            Quantity = quantity,
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
                x.AddConsumer<LatestValuationRecorderConsumer>();
            },
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }

    private sealed class RebuildProbeConsumer : IConsumer<HistoryRebuildRequested>
    {
        public Task Consume(ConsumeContext<HistoryRebuildRequested> context)
        {
            RequestedUsers.Add(context.Message.UserId);
            return Task.CompletedTask;
        }
    }

    private sealed class RebuildProbeDefinition : ConsumerDefinition<RebuildProbeConsumer>
    {
        public RebuildProbeDefinition() => Endpoint(e => e.Name = ProbeQueueName);
    }
}
