using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.PositionEvents;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// Builds its own provider, with no HTTP host, on a queue unique to this class.
public sealed class HistoryRebuildConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "history-rebuild-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Rebuild_WritesEveryDayToYesterday_LeavesTodayAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-5), cancellationToken);
            await db.SeedValuationLineAsync(userId, accountId, assetId, today, 999m, cancellationToken, quantity: 1_000m);
            await db.SeedSnapshotAsync(userId, today, 999m, cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("USDPLN", (today.AddDays(-6), 4.0m), (today.AddDays(-3), 4.5m));

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, userId, cancellationToken);

            for (var offset = 5; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                var expectedRate = offset >= 4 ? 4.0m : 4.5m;

                var line = Assert.Single(await GetLinesAsync(containers, accountId, date, cancellationToken));
                Assert.Equal(1_000m * expectedRate, line.ValuePln);
                Assert.Equal(expectedRate, line.FxRateUsed);
                Assert.False(line.IsStale);

                var snapshot = await GetSnapshotAsync(containers, userId, date, cancellationToken);
                Assert.NotNull(snapshot);
                Assert.Equal(1_000m * expectedRate, snapshot.TotalPln);
                Assert.Equal(userId, snapshot.UserId);
            }

            Assert.Null(await GetSnapshotAsync(containers, userId, today.AddDays(-6), cancellationToken));
            Assert.Equal(999m, Assert.Single(await GetLinesAsync(containers, accountId, today, cancellationToken)).ValuePln);
            Assert.Equal(999m, (await GetSnapshotAsync(containers, userId, today, cancellationToken))!.TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_StartMovedLater_DeletesEarlierRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-8), 100m)]);
            for (var offset = 20; offset >= 9; offset--)
            {
                await db.SeedValuationLineAsync(userId, accountId, assetId, today.AddDays(-offset), 100m, cancellationToken, quantity: 100m);
                await db.SeedSnapshotAsync(userId, today.AddDays(-offset), 100m, cancellationToken);
            }

            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-20), cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, userId, cancellationToken);

            var lines = await GetAllLinesAsync(containers, accountId, cancellationToken);
            var snapshots = await GetAllSnapshotsAsync(containers, userId, cancellationToken);

            var expectedDates = Enumerable.Range(1, 8).Select(offset => today.AddDays(-offset)).Order().ToList();
            Assert.Equal(expectedDates, lines.Select(l => l.Date).Order());
            Assert.Equal(expectedDates, snapshots.Select(s => s.Date).Order());
            Assert.All(snapshots, s => Assert.Equal(100m, s.TotalPln));
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_ArchivedAsset_NoLineFromArchiveDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var archivedAssetId = Guid.NewGuid();
        var keptAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(archivedAssetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                isArchived: true, archivedOn: today.AddDays(-5), quantityHistory: [(today.AddDays(-20), 100m)]);
            await db.SeedPositionAsync(keptAssetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 200m,
                quantityHistory: [(today.AddDays(-20), 200m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-8), cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, userId, cancellationToken);

            for (var offset = 8; offset >= 6; offset--)
            {
                var date = today.AddDays(-offset);
                Assert.Equal(2, (await GetLinesAsync(containers, accountId, date, cancellationToken)).Count);
                Assert.Equal(300m, (await GetSnapshotAsync(containers, userId, date, cancellationToken))!.TotalPln);
            }

            for (var offset = 5; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                var line = Assert.Single(await GetLinesAsync(containers, accountId, date, cancellationToken));
                Assert.Equal(keptAssetId, line.AssetId);
                Assert.Equal(200m, (await GetSnapshotAsync(containers, userId, date, cancellationToken))!.TotalPln);
            }
        }, cancellationToken);
    }

    [Fact]
    public async Task RebuildsEveryPositionOfTheUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountAId = Guid.NewGuid();
        var accountBId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetAId, userId, accountAId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-20), 100m)]);
            await db.SeedPositionAsync(assetBId, userId, accountBId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 200m,
                quantityHistory: [(today.AddDays(-20), 200m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-10), cancellationToken, revision: 1);
        }

        await RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<AssetPositionChangedConsumer>(typeof(PositionChangedTestDefinition));
                x.AddConsumer<HistoryRebuildConsumer>(typeof(TestConsumerDefinition));
            },
            async provider =>
            {
                await provider.GetRequiredService<IBus>().Publish(
                    new AssetPositionChanged
                    {
                        AssetId = assetAId,
                        AccountId = accountAId,
                        UserId = userId,
                        AssetClass = AssetClass.Cash,
                        ValuationMode = AssetValuationMode.CurrencyValued,
                        Currency = "PLN",
                        Quantity = 150m,
                        QuoteUnitsPerQuantity = 1m,
                        ClassHistory = [],
                        QuantityHistory =
                        [
                            new QuantityPoint { Date = today.AddDays(-20), Quantity = 100m },
                            new QuantityPoint { Date = today.AddDays(-5), Quantity = 150m },
                        ],
                        IsArchived = false,
                        Version = 1,
                        OccurredAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);

                await WaitForPositionAsync(provider, assetAId, cancellationToken, p => p.Version == 1);
                await WaitForNoRebuildRequestAsync(provider, userId, cancellationToken);

                Assert.Empty(await GetUserLinesAsync(containers, userId, today.AddDays(-11), cancellationToken));

                for (var offset = 10; offset >= 1; offset--)
                {
                    var date = today.AddDays(-offset);
                    var expectedA = offset > 5 ? 100m : 150m;

                    var lines = await GetUserLinesAsync(containers, userId, date, cancellationToken);
                    Assert.Equal(2, lines.Count);
                    Assert.Equal(accountAId, lines.Single(l => l.AssetId == assetAId).AccountId);
                    Assert.Equal(accountBId, lines.Single(l => l.AssetId == assetBId).AccountId);
                    Assert.Equal(expectedA, lines.Single(l => l.AssetId == assetAId).ValuePln);
                    Assert.Equal(200m, lines.Single(l => l.AssetId == assetBId).ValuePln);

                    var snapshot = await GetSnapshotAsync(containers, userId, date, cancellationToken);
                    Assert.NotNull(snapshot);
                    Assert.Equal(expectedA + 200m, snapshot.TotalPln);
                }

                Assert.Equal(10, (await GetAllSnapshotsAsync(containers, userId, cancellationToken)).Count(s => s.Date < today));
            },
            cancellationToken);
    }

    [Fact]
    public async Task NoPendingRequest_DoesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedValuationLineAsync(userId, accountId, assetId, today.AddDays(-3), 999m, cancellationToken);
            await db.SeedSnapshotAsync(userId, today.AddDays(-3), 999m, cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("USDPLN", (today.AddDays(-10), 4.0m));

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);

            // Nothing signals a no-op, so give the consumer time to run before asserting it wrote nothing.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Equal(0, client.HistoryCalls);
            Assert.Equal(999m, Assert.Single(await GetAllLinesAsync(containers, accountId, cancellationToken)).ValuePln);
            Assert.Equal(999m, Assert.Single(await GetAllSnapshotsAsync(containers, userId, cancellationToken)).TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task SameMessageIdTwice_RebuildsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-3), cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("USDPLN", (today.AddDays(-10), 4.0m));

        await RunConsumerAsync(client, async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();

            await bus.PublishRebuildRequestedAsync(userId, cancellationToken, messageId);
            await WaitForNoRebuildRequestAsync(provider, userId, cancellationToken);
            var callsAfterFirstRun = client.HistoryCalls;
            Assert.True(callsAfterFirstRun > 0);

            await using (var db = OpenDbContext(containers))
            {
                await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-3), cancellationToken);
            }

            // Same MessageId: the inbox must skip the consumer body, so the fresh request stays pending.
            await bus.PublishRebuildRequestedAsync(userId, cancellationToken, messageId);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.NotNull(await GetRebuildRequestAsync(containers, userId, cancellationToken));
            Assert.Equal(callsAfterFirstRun, client.HistoryCalls);
            Assert.Equal(3, (await GetAllSnapshotsAsync(containers, userId, cancellationToken)).Count);
        }, cancellationToken);
    }

    [Fact]
    public async Task RevisionChanged_KeepsRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-3), cancellationToken, revision: 1);
        }

        var bumped = 0;
        var client = new FakePriceQuoteClient()
            .WithFxHistory("USDPLN", (today.AddDays(-10), 4.0m))
            .OnHistoryFetched(async () =>
            {
                if (Interlocked.Exchange(ref bumped, 1) == 1)
                {
                    return;
                }

                await using var db = OpenDbContext(containers);
                await db.HistoryRebuildRequests.IgnoreQueryFilters()
                    .Where(r => r.UserId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Revision, r => r.Revision + 1), cancellationToken);
            });

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);
            await WaitForSnapshotAsync(provider, userId, today.AddDays(-1), cancellationToken);

            var request = await GetRebuildRequestAsync(containers, userId, cancellationToken);
            Assert.NotNull(request);
            Assert.Equal(2, request.Revision);
            Assert.Equal(today.AddDays(-3), request.FromDate);
        }, cancellationToken);
    }

    [Fact]
    public async Task MarketDataDown_KeepsRequestAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedValuationLineAsync(userId, accountId, assetId, today.AddDays(-3), 999m, cancellationToken);
            await db.SeedSnapshotAsync(userId, today.AddDays(-3), 999m, cancellationToken);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-5), cancellationToken);
        }

        var client = new FakePriceQuoteClient().Unavailable();

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);

            await WaitForAsync(
                provider,
                (_, _) => Task.FromResult(client.HistoryCalls),
                calls => calls > 0,
                "The rebuild asking MarketData for history",
                cancellationToken);

            // The failure is retried and then parked; give it time to write anything it wrongly would.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var request = await GetRebuildRequestAsync(containers, userId, cancellationToken);
            Assert.NotNull(request);
            Assert.Equal(today.AddDays(-5), request.FromDate);
            Assert.Equal(999m, Assert.Single(await GetAllLinesAsync(containers, accountId, cancellationToken)).ValuePln);
            Assert.Equal(999m, Assert.Single(await GetAllSnapshotsAsync(containers, userId, cancellationToken)).TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task OtherUserUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var accountAId = Guid.NewGuid();
        var accountBId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetAId, userAId, accountAId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-20), 100m)]);
            await db.SeedHistoryRebuildRequestAsync(userAId, today.AddDays(-5), cancellationToken);

            await db.SeedPositionAsync(assetBId, userBId, accountBId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 200m,
                quantityHistory: [(today.AddDays(-20), 200m)]);
            await db.SeedValuationLineAsync(userBId, accountBId, assetBId, today.AddDays(-3), 777m, cancellationToken);
            await db.SeedSnapshotAsync(userBId, today.AddDays(-3), 777m, cancellationToken);
            await db.SeedHistoryRebuildRequestAsync(userBId, today.AddDays(-5), cancellationToken, revision: 2);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userAId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, userAId, cancellationToken);

            var snapshotsA = await GetAllSnapshotsAsync(containers, userAId, cancellationToken);
            Assert.Equal(5, snapshotsA.Count);
            Assert.All(snapshotsA, s => Assert.Equal(100m, s.TotalPln));
            Assert.All(snapshotsA, s => Assert.Equal(userAId, s.UserId));

            var lineB = Assert.Single(await GetAllLinesAsync(containers, accountBId, cancellationToken));
            Assert.Equal(777m, lineB.ValuePln);
            var snapshotB = Assert.Single(await GetAllSnapshotsAsync(containers, userBId, cancellationToken));
            Assert.Equal(777m, snapshotB.TotalPln);

            var requestB = await GetRebuildRequestAsync(containers, userBId, cancellationToken);
            Assert.NotNull(requestB);
            Assert.Equal(2, requestB.Revision);
            Assert.Equal(today.AddDays(-5), requestB.FromDate);
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_RemovedAsset_DropsItsLines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var removedAssetId = Guid.NewGuid();
        var keptAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(removedAssetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-30), 100m)]);
            await db.SeedPositionAsync(keptAssetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 200m,
                quantityHistory: [(today.AddDays(-30), 200m)]);

            for (var offset = 30; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                await db.SeedValuationLineAsync(userId, accountId, removedAssetId, date, 100m, cancellationToken, quantity: 100m);
                await db.SeedValuationLineAsync(userId, accountId, keptAssetId, date, 200m, cancellationToken, quantity: 200m);
                await db.SeedSnapshotAsync(userId, date, 300m, cancellationToken);
            }
        }

        await RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<AssetRemovedConsumer>(typeof(AssetRemovedTestDefinition));
                x.AddConsumer<HistoryRebuildConsumer>(typeof(TestConsumerDefinition));
            },
            async provider =>
            {
                await provider.GetRequiredService<IBus>().Publish(AssetRemovedEvent(removedAssetId, accountId, userId), cancellationToken);

                // The request AssetRemoved leaves behind is rebuilt; the rebuild deletes the removed asset's lines in the same commit.
                await WaitForAsync(
                    provider,
                    (db, ct) => db.AssetValuations.IgnoreQueryFilters().AnyAsync(l => l.AssetId == removedAssetId, ct),
                    stillExists => !stillExists,
                    $"Rebuilding away the lines of removed asset {removedAssetId}",
                    cancellationToken);

                var lines = await GetAllLinesAsync(containers, accountId, cancellationToken);
                Assert.DoesNotContain(lines, l => l.AssetId == removedAssetId);
                Assert.Equal(30, lines.Count(l => l.Date < today));

                var snapshots = (await GetAllSnapshotsAsync(containers, userId, cancellationToken))
                    .Where(s => s.Date < today)
                    .ToList();
                Assert.Equal(30, snapshots.Count);
                Assert.All(snapshots, s => Assert.Equal(200m, s.TotalPln));
            },
            cancellationToken);
    }

    [Fact]
    public async Task Rebuild_StoredSnapshotTotalEqualsSumOfStoredLines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await using (var db = OpenDbContext(containers))
        {
            foreach (var assetId in assetIds)
            {
                await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                    assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 33.3333m,
                    quantityHistory: [(today.AddDays(-20), 33.3333m)]);
            }

            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-3), cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("USDPLN", (today.AddDays(-6), 4.0123m));

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, userId, cancellationToken);

            for (var offset = 3; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                var lines = await GetLinesAsync(containers, accountId, date, cancellationToken);
                var snapshot = await GetSnapshotAsync(containers, userId, date, cancellationToken);

                Assert.Equal(3, lines.Count);
                Assert.NotNull(snapshot);
                Assert.Equal(lines.Sum(l => l.ValuePln), snapshot.TotalPln);
            }
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_PublishesLatestValuation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-8), 100m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-3), cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);

            var published = Assert.Single(await provider.GetRequiredService<LatestValuationRecorder>()
                .SettledDeliveriesForAsync(userId, cancellationToken));

            Assert.Equal(today.AddDays(-1), published.Date);
            Assert.Equal(100m, published.TotalPln);
            var line = Assert.Single(published.ByAssetClass);
            Assert.Equal(AssetClass.Cash, line.AssetClass);
            Assert.Equal(100m, line.ValuePln);
        }, cancellationToken);
    }

    [Fact]
    public async Task StampsClassPerDay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await SeedFreeCashAsync(db, assetId, userId, accountId, today, cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, userId, cancellationToken);

            for (var offset = 10; offset >= 1; offset--)
            {
                var expected = offset >= 7 ? AssetClass.Cash : offset >= 4 ? AssetClass.Stock : AssetClass.Etf;
                var line = Assert.Single(await GetLinesAsync(containers, accountId, today.AddDays(-offset), cancellationToken));
                Assert.Equal(expected, line.AssetClass);
                Assert.Equal(400m, line.ValuePln);
            }
        }, cancellationToken);
    }

    [Fact]
    public async Task PublishedByAssetClass_FollowsTimeline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await SeedFreeCashAsync(db, assetId, userId, accountId, today, cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(userId, cancellationToken);

            var published = Assert.Single(await provider.GetRequiredService<LatestValuationRecorder>()
                .SettledDeliveriesForAsync(userId, cancellationToken));

            Assert.Equal(today.AddDays(-1), published.Date);
            var line = Assert.Single(published.ByAssetClass);
            Assert.Equal(AssetClass.Etf, line.AssetClass);
            Assert.Equal(400m, line.ValuePln);
        }, cancellationToken);
    }

    private static async Task SeedFreeCashAsync(
        ReportingDbContext db, Guid assetId, Guid userId, Guid accountId, DateOnly today, CancellationToken cancellationToken)
    {
        await db.SeedPositionAsync(assetId, userId, accountId, cancellationToken,
            assetClass: AssetClass.Etf, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 400m,
            quantityHistory: [(today.AddDays(-10), 400m)],
            classHistory: [(today.AddDays(-10), AssetClass.Cash), (today.AddDays(-6), AssetClass.Stock), (today.AddDays(-3), AssetClass.Etf)]);
        await db.SeedHistoryRebuildRequestAsync(userId, today.AddDays(-10), cancellationToken);
    }

    private Task RunConsumerAsync(
        IPriceQuoteClient priceQuoteClient, Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<HistoryRebuildConsumer>(typeof(TestConsumerDefinition));
                x.AddConsumer<LatestValuationRecorderConsumer>();
            },
            action,
            cancellationToken,
            priceQuoteClient);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<HistoryRebuildConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }

    private sealed class PositionChangedTestDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, ReportingDbContext>
    {
        public PositionChangedTestDefinition() => Endpoint(e => e.Name = "history-rebuild-position-changed-test");
    }

    private sealed class AssetRemovedTestDefinition : IdempotentConsumerDefinition<AssetRemovedConsumer, ReportingDbContext>
    {
        public AssetRemovedTestDefinition() => Endpoint(e => e.Name = "history-rebuild-asset-removed-test");
    }
}
