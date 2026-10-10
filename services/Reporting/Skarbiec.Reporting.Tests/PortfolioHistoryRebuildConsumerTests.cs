using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
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
public sealed class PortfolioHistoryRebuildConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "portfolio-history-rebuild-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Rebuild_WritesEveryDayToYesterday_LeavesTodayAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-5), cancellationToken);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, today, 999m, cancellationToken, quantity: 1_000m);
            await db.SeedSnapshotAsync(userId, portfolioId, today, 999m, cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("USDPLN", (today.AddDays(-6), 4.0m), (today.AddDays(-3), 4.5m));

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, portfolioId, cancellationToken);

            for (var offset = 5; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                var expectedRate = offset >= 4 ? 4.0m : 4.5m;

                var line = Assert.Single(await GetLinesAsync(containers, portfolioId, date, cancellationToken));
                Assert.Equal(1_000m * expectedRate, line.ValuePln);
                Assert.Equal(expectedRate, line.FxRateUsed);
                Assert.False(line.IsStale);

                var snapshot = await GetSnapshotAsync(containers, portfolioId, date, cancellationToken);
                Assert.NotNull(snapshot);
                Assert.Equal(1_000m * expectedRate, snapshot.TotalPln);
                Assert.Equal(userId, snapshot.UserId);
            }

            Assert.Null(await GetSnapshotAsync(containers, portfolioId, today.AddDays(-6), cancellationToken));
            Assert.Equal(999m, Assert.Single(await GetLinesAsync(containers, portfolioId, today, cancellationToken)).ValuePln);
            Assert.Equal(999m, (await GetSnapshotAsync(containers, portfolioId, today, cancellationToken))!.TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_StartMovedLater_DeletesEarlierRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-8), 100m)]);
            for (var offset = 20; offset >= 9; offset--)
            {
                await db.SeedValuationLineAsync(userId, portfolioId, assetId, today.AddDays(-offset), 100m, cancellationToken, quantity: 100m);
                await db.SeedSnapshotAsync(userId, portfolioId, today.AddDays(-offset), 100m, cancellationToken);
            }

            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-20), cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, portfolioId, cancellationToken);

            var lines = await GetAllLinesAsync(containers, portfolioId, cancellationToken);
            var snapshots = await GetAllSnapshotsAsync(containers, portfolioId, cancellationToken);

            var expectedDates = Enumerable.Range(1, 8).Select(offset => today.AddDays(-offset)).Order().ToList();
            Assert.Equal(expectedDates, lines.Select(l => l.Date).Order());
            Assert.Equal(expectedDates, snapshots.Select(s => s.Date).Order());
            Assert.All(snapshots, s => Assert.Equal(100m, s.TotalPln));
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_Manual_ValuesFromManualValueDateAndKeepsEarlierLines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.RealEstate, valuationMode: AssetValuationMode.Manual, currency: "EUR", quantity: 1m,
                manualValueAmount: 500_000m, manualValueDate: today.AddDays(-10));
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, today.AddDays(-15), 450_000m, cancellationToken,
                assetClass: AssetClass.RealEstate, fxRateUsed: 4.0m);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-20), cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("EURPLN", (today.AddDays(-12), 4.2m));

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, portfolioId, cancellationToken);

            var kept = Assert.Single(await GetLinesAsync(containers, portfolioId, today.AddDays(-15), cancellationToken));
            Assert.Equal(450_000m, kept.ValuePln);
            Assert.Equal(4.0m, kept.FxRateUsed);

            for (var offset = 14; offset >= 11; offset--)
            {
                Assert.Empty(await GetLinesAsync(containers, portfolioId, today.AddDays(-offset), cancellationToken));
            }

            for (var offset = 10; offset >= 1; offset--)
            {
                var line = Assert.Single(await GetLinesAsync(containers, portfolioId, today.AddDays(-offset), cancellationToken));
                Assert.Equal(500_000m * 4.2m, line.ValuePln);

                var snapshot = await GetSnapshotAsync(containers, portfolioId, today.AddDays(-offset), cancellationToken);
                Assert.Equal(500_000m * 4.2m, snapshot!.TotalPln);
            }
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_ArchivedPortfolio_ZeroFromArchiveDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                portfolioIsArchived: true, portfolioArchivedOn: today.AddDays(-5), quantityHistory: [(today.AddDays(-20), 100m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-10), cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, portfolioId, cancellationToken);

            for (var offset = 10; offset >= 6; offset--)
            {
                var date = today.AddDays(-offset);
                Assert.Equal(100m, Assert.Single(await GetLinesAsync(containers, portfolioId, date, cancellationToken)).ValuePln);
                Assert.Equal(100m, (await GetSnapshotAsync(containers, portfolioId, date, cancellationToken))!.TotalPln);
            }

            for (var offset = 5; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                Assert.Empty(await GetLinesAsync(containers, portfolioId, date, cancellationToken));

                var snapshot = await GetSnapshotAsync(containers, portfolioId, date, cancellationToken);
                Assert.NotNull(snapshot);
                Assert.Equal(0m, snapshot.TotalPln);
                Assert.False(snapshot.IsStale);
            }
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_ArchivedAsset_NoLineFromArchiveDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var archivedAssetId = Guid.NewGuid();
        var keptAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(archivedAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                isArchived: true, archivedOn: today.AddDays(-5), quantityHistory: [(today.AddDays(-20), 100m)]);
            await db.SeedPositionAsync(keptAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 200m,
                quantityHistory: [(today.AddDays(-20), 200m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-8), cancellationToken);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, portfolioId, cancellationToken);

            for (var offset = 8; offset >= 6; offset--)
            {
                var date = today.AddDays(-offset);
                Assert.Equal(2, (await GetLinesAsync(containers, portfolioId, date, cancellationToken)).Count);
                Assert.Equal(300m, (await GetSnapshotAsync(containers, portfolioId, date, cancellationToken))!.TotalPln);
            }

            for (var offset = 5; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                var line = Assert.Single(await GetLinesAsync(containers, portfolioId, date, cancellationToken));
                Assert.Equal(keptAssetId, line.AssetId);
                Assert.Equal(200m, (await GetSnapshotAsync(containers, portfolioId, date, cancellationToken))!.TotalPln);
            }
        }, cancellationToken);
    }

    [Fact]
    public async Task NoPendingRequest_DoesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, today.AddDays(-3), 999m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, today.AddDays(-3), 999m, cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("USDPLN", (today.AddDays(-10), 4.0m));

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);

            // Nothing signals a no-op, so give the consumer time to run before asserting it wrote nothing.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Equal(0, client.HistoryCalls);
            Assert.Equal(999m, Assert.Single(await GetAllLinesAsync(containers, portfolioId, cancellationToken)).ValuePln);
            Assert.Equal(999m, Assert.Single(await GetAllSnapshotsAsync(containers, portfolioId, cancellationToken)).TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task SameMessageIdTwice_RebuildsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-3), cancellationToken);
        }

        var client = new FakePriceQuoteClient().WithFxHistory("USDPLN", (today.AddDays(-10), 4.0m));

        await RunConsumerAsync(client, async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();

            await bus.PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken, messageId);
            await WaitForNoRebuildRequestAsync(provider, portfolioId, cancellationToken);
            var callsAfterFirstRun = client.HistoryCalls;
            Assert.True(callsAfterFirstRun > 0);

            await using (var db = OpenDbContext(containers))
            {
                await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-3), cancellationToken);
            }

            // Same MessageId: the inbox must skip the consumer body, so the fresh request stays pending.
            await bus.PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken, messageId);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.NotNull(await GetRebuildRequestAsync(containers, portfolioId, cancellationToken));
            Assert.Equal(callsAfterFirstRun, client.HistoryCalls);
            Assert.Equal(3, (await GetAllSnapshotsAsync(containers, portfolioId, cancellationToken)).Count);
        }, cancellationToken);
    }

    [Fact]
    public async Task RevisionChanged_KeepsRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-3), cancellationToken, revision: 1);
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
                    .Where(r => r.PortfolioId == portfolioId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Revision, r => r.Revision + 1), cancellationToken);
            });

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, today.AddDays(-1), cancellationToken);

            var request = await GetRebuildRequestAsync(containers, portfolioId, cancellationToken);
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
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "USD", quantity: 1_000m,
                quantityHistory: [(today.AddDays(-20), 1_000m)]);
            await db.SeedValuationLineAsync(userId, portfolioId, assetId, today.AddDays(-3), 999m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, today.AddDays(-3), 999m, cancellationToken);
            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, today.AddDays(-5), cancellationToken);
        }

        var client = new FakePriceQuoteClient().Unavailable();

        await RunConsumerAsync(client, async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);

            await WaitForAsync(
                provider,
                (_, _) => Task.FromResult(client.HistoryCalls),
                calls => calls > 0,
                "The rebuild asking MarketData for history",
                cancellationToken);

            // The failure is retried and then parked; give it time to write anything it wrongly would.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var request = await GetRebuildRequestAsync(containers, portfolioId, cancellationToken);
            Assert.NotNull(request);
            Assert.Equal(today.AddDays(-5), request.FromDate);
            Assert.Equal(999m, Assert.Single(await GetAllLinesAsync(containers, portfolioId, cancellationToken)).ValuePln);
            Assert.Equal(999m, Assert.Single(await GetAllSnapshotsAsync(containers, portfolioId, cancellationToken)).TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Rebuild_NeverTouchesAnotherUsersRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var portfolioAId = Guid.NewGuid();
        var portfolioBId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetAId, userAId, portfolioAId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-20), 100m)]);
            await db.SeedHistoryRebuildRequestAsync(userAId, portfolioAId, today.AddDays(-5), cancellationToken);

            await db.SeedPositionAsync(assetBId, userBId, portfolioBId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 200m,
                quantityHistory: [(today.AddDays(-20), 200m)]);
            await db.SeedValuationLineAsync(userBId, portfolioBId, assetBId, today.AddDays(-3), 777m, cancellationToken);
            await db.SeedSnapshotAsync(userBId, portfolioBId, today.AddDays(-3), 777m, cancellationToken);
            await db.SeedHistoryRebuildRequestAsync(userBId, portfolioBId, today.AddDays(-5), cancellationToken, revision: 2);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioAId, userAId, cancellationToken);
            await WaitForNoRebuildRequestAsync(provider, portfolioAId, cancellationToken);

            var snapshotsA = await GetAllSnapshotsAsync(containers, portfolioAId, cancellationToken);
            Assert.Equal(5, snapshotsA.Count);
            Assert.All(snapshotsA, s => Assert.Equal(100m, s.TotalPln));
            Assert.All(snapshotsA, s => Assert.Equal(userAId, s.UserId));

            var lineB = Assert.Single(await GetAllLinesAsync(containers, portfolioBId, cancellationToken));
            Assert.Equal(777m, lineB.ValuePln);
            var snapshotB = Assert.Single(await GetAllSnapshotsAsync(containers, portfolioBId, cancellationToken));
            Assert.Equal(777m, snapshotB.TotalPln);

            var requestB = await GetRebuildRequestAsync(containers, portfolioBId, cancellationToken);
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
        var portfolioId = Guid.NewGuid();
        var removedAssetId = Guid.NewGuid();
        var keptAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(removedAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 100m,
                quantityHistory: [(today.AddDays(-30), 100m)]);
            await db.SeedPositionAsync(keptAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued, currency: "PLN", quantity: 200m,
                quantityHistory: [(today.AddDays(-30), 200m)]);

            for (var offset = 30; offset >= 1; offset--)
            {
                var date = today.AddDays(-offset);
                await db.SeedValuationLineAsync(userId, portfolioId, removedAssetId, date, 100m, cancellationToken, quantity: 100m);
                await db.SeedValuationLineAsync(userId, portfolioId, keptAssetId, date, 200m, cancellationToken, quantity: 200m);
                await db.SeedSnapshotAsync(userId, portfolioId, date, 300m, cancellationToken);
            }
        }

        await RunAsync(
            containers,
            x =>
            {
                x.AddConsumer<AssetRemovedConsumer>(typeof(AssetRemovedTestDefinition));
                x.AddConsumer<PortfolioHistoryRebuildConsumer>(typeof(TestConsumerDefinition));
            },
            async provider =>
            {
                await provider.GetRequiredService<IBus>().Publish(AssetRemovedEvent(removedAssetId, portfolioId, userId), cancellationToken);

                // The request AssetRemoved leaves behind is rebuilt; the rebuild deletes the removed asset's lines in the same commit.
                await WaitForAsync(
                    provider,
                    (db, ct) => db.AssetValuations.IgnoreQueryFilters().AnyAsync(l => l.AssetId == removedAssetId, ct),
                    stillExists => !stillExists,
                    $"Rebuilding away the lines of removed asset {removedAssetId}",
                    cancellationToken);

                var lines = await GetAllLinesAsync(containers, portfolioId, cancellationToken);
                Assert.DoesNotContain(lines, l => l.AssetId == removedAssetId);
                Assert.Equal(30, lines.Count(l => l.Date < today));

                var snapshots = (await GetAllSnapshotsAsync(containers, portfolioId, cancellationToken))
                    .Where(s => s.Date < today)
                    .ToList();
                Assert.Equal(30, snapshots.Count);
                Assert.All(snapshots, s => Assert.Equal(200m, s.TotalPln));
            },
            cancellationToken);
    }

    private Task RunConsumerAsync(
        IPriceQuoteClient priceQuoteClient, Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x => x.AddConsumer<PortfolioHistoryRebuildConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken,
            priceQuoteClient);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<PortfolioHistoryRebuildConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }

    private sealed class AssetRemovedTestDefinition : IdempotentConsumerDefinition<AssetRemovedConsumer, ReportingDbContext>
    {
        public AssetRemovedTestDefinition() => Endpoint(e => e.Name = "portfolio-history-rebuild-asset-removed-test");
    }
}
