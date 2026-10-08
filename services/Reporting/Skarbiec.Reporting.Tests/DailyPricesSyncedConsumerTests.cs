using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Reporting.Valuation;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// No positions client is registered at all: values come from the local Position table alone.
public sealed class DailyPricesSyncedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "daily-prices-synced-consumer-test";

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Consume_DailyPricesSynced_ValuesEveryPortfolioFromLocalPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var stockInstrumentId = Guid.NewGuid();

        var userAId = Guid.NewGuid();
        var portfolioAId = Guid.NewGuid();
        var assetAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var portfolioBId = Guid.NewGuid();
        var assetBId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetAId, userAId, portfolioAId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market,
                instrumentId: stockInstrumentId, currency: "PLN", quantity: 10m);

            await db.SeedPositionAsync(assetBId, userBId, portfolioBId, cancellationToken,
                assetClass: AssetClass.RealEstate, valuationMode: AssetValuationMode.Manual,
                currency: "PLN", quantity: 0m, manualValueAmount: 300_000m);
        }

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithPrice(stockInstrumentId, new InstrumentPriceLookup("PLN", snapshotDate, 150m));

        await RunConsumerAsync(priceQuoteClient, async provider =>
        {
            await PublishSyncAsync(provider, snapshotDate, cancellationToken);

            var snapshotA = await WaitForSnapshotAsync(provider, portfolioAId, snapshotDate, cancellationToken);
            var snapshotB = await WaitForSnapshotAsync(provider, portfolioBId, snapshotDate, cancellationToken);

            Assert.Equal(userAId, snapshotA.UserId);
            Assert.Equal(1_500m, snapshotA.TotalPln);

            Assert.Equal(userBId, snapshotB.UserId);
            Assert.Equal(300_000m, snapshotB.TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_DailyPricesSynced_SkipsArchivedPortfolios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var userId = Guid.NewGuid();
        var archivedPortfolioId = Guid.NewGuid();
        var archivedAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(archivedAssetId, userId, archivedPortfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 1_000m, portfolioIsArchived: true);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await PublishSyncAsync(provider, snapshotDate, cancellationToken);

            // Nothing to wait on for a portfolio that must never get a snapshot, so give the consumer a full run's time.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            Assert.False(await db.ValuationSnapshots.IgnoreQueryFilters()
                .AnyAsync(s => s.PortfolioId == archivedPortfolioId, cancellationToken));
            Assert.False(await db.AssetValuations.IgnoreQueryFilters()
                .AnyAsync(l => l.AssetId == archivedAssetId, cancellationToken));
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SkipsArchivedAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var archivedAssetId = Guid.NewGuid();
        var liveAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(archivedAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 1_000m, isArchived: true);
            await db.SeedPositionAsync(liveAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "PLN", quantity: 50m);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            await PublishSyncAsync(provider, snapshotDate, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            Assert.Equal(50m, snapshot.TotalPln);
            var line = Assert.Single(await GetLinesAsync(containers, portfolioId, snapshotDate, cancellationToken));
            Assert.Equal(liveAssetId, line.AssetId);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_DailyPricesSynced_WritesOneLinePerPositionSummingToTheSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var stockInstrumentId = Guid.NewGuid();

        var marketAssetId = Guid.NewGuid();
        var manualAssetId = Guid.NewGuid();
        var cashAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(marketAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market,
                instrumentId: stockInstrumentId, currency: "PLN", quantity: 10m);

            await db.SeedPositionAsync(manualAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.RealEstate, valuationMode: AssetValuationMode.Manual,
                currency: "PLN", quantity: 0m, manualValueAmount: 300_000m);

            await db.SeedPositionAsync(cashAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "EUR", quantity: 1_000m);
        }

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithPrice(stockInstrumentId, new InstrumentPriceLookup("PLN", snapshotDate, 150m))
            .WithFxRate("EURPLN", new FxRateLookup(snapshotDate, 4.30m));

        await RunConsumerAsync(priceQuoteClient, async provider =>
        {
            await PublishSyncAsync(provider, snapshotDate, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var lines = await db.AssetValuations.IgnoreQueryFilters()
                .Where(l => l.PortfolioId == portfolioId && l.Date == snapshotDate)
                .ToListAsync(cancellationToken);

            Assert.Equal(3, lines.Count);

            var marketLine = Assert.Single(lines, l => l.AssetId == marketAssetId);
            Assert.Equal(10m, marketLine.Quantity);
            Assert.Equal(150m, marketLine.PriceUsed);
            Assert.Equal(snapshotDate, marketLine.PriceDate);
            Assert.Equal(1_500m, marketLine.ValuePln);

            var manualLine = Assert.Single(lines, l => l.AssetId == manualAssetId);
            Assert.Null(manualLine.PriceUsed);
            Assert.Equal(300_000m, manualLine.ValuePln);

            var cashLine = Assert.Single(lines, l => l.AssetId == cashAssetId);
            Assert.Equal(4.30m, cashLine.FxRateUsed);
            Assert.Equal(4_300m, cashLine.ValuePln);

            Assert.Equal(lines.Sum(l => l.ValuePln), snapshot.TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_WithQuoteOlderThanSevenDays_MarksOnlyThatLineAndTheSnapshotStale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 20);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        var staleInstrumentId = Guid.NewGuid();
        var staleAssetId = Guid.NewGuid();
        var freshInstrumentId = Guid.NewGuid();
        var freshAssetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(staleAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market,
                instrumentId: staleInstrumentId, currency: "PLN", quantity: 1m);

            await db.SeedPositionAsync(freshAssetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Etf, valuationMode: AssetValuationMode.Market,
                instrumentId: freshInstrumentId, currency: "PLN", quantity: 1m);
        }

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithPrice(staleInstrumentId, new InstrumentPriceLookup("PLN", snapshotDate.AddDays(-10), 100m))
            .WithPrice(freshInstrumentId, new InstrumentPriceLookup("PLN", snapshotDate, 50m));

        await RunConsumerAsync(priceQuoteClient, async provider =>
        {
            await PublishSyncAsync(provider, snapshotDate, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);
            Assert.True(snapshot.IsStale);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var lines = await db.AssetValuations.IgnoreQueryFilters()
                .Where(l => l.PortfolioId == portfolioId && l.Date == snapshotDate)
                .ToListAsync(cancellationToken);

            Assert.True(Assert.Single(lines, l => l.AssetId == staleAssetId).IsStale);
            Assert.False(Assert.Single(lines, l => l.AssetId == freshAssetId).IsStale);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_SameMessageIdTwice_LeavesOneSnapshotAndOneLinePerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 11);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.Manual,
                currency: "PLN", quantity: 0m, manualValueAmount: 42m);
        }

        await RunConsumerAsync(new FakePriceQuoteClient(), async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            var messageId = Guid.NewGuid();
            var @event = new DailyPricesSynced
            {
                RunId = Guid.NewGuid(),
                SyncDate = snapshotDate,
                SyncedCount = 1,
                FailedCount = 0,
                NoDataCount = 0,
            };

            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            // Same MessageId: the inbox must skip the consumer body.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // Nothing signals a skip, so give a redelivery time to land before asserting it never did.
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

            var snapshots = await db.ValuationSnapshots.IgnoreQueryFilters()
                .Where(s => s.PortfolioId == portfolioId && s.Date == snapshotDate)
                .ToListAsync(cancellationToken);
            Assert.Single(snapshots);

            var lines = await db.AssetValuations.IgnoreQueryFilters()
                .Where(l => l.AssetId == assetId && l.Date == snapshotDate)
                .ToListAsync(cancellationToken);
            Assert.Single(lines);
        }, cancellationToken);
    }

    // Both kinds recompute, and the (PortfolioId, Date) upsert keeps a same-day pair idempotent.
    [Fact]
    public async Task Consume_FxKind_RecomputesSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(assetId, userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "EUR", quantity: 100m);
        }

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithFxRate("EURPLN", new FxRateLookup(snapshotDate, 4.30m));

        await RunConsumerAsync(priceQuoteClient, async provider =>
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Publish(new DailyPricesSynced
            {
                RunId = Guid.NewGuid(),
                SyncDate = snapshotDate,
                SyncedCount = 1,
                FailedCount = 0,
                NoDataCount = 0,
                Kind = PriceSyncKind.Fx,
            }, cancellationToken);

            var snapshot = await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            Assert.Equal(430m, snapshot.TotalPln);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_StoresLatestPricesAndFxRatesLocally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market,
                instrumentId: instrumentId, currency: "USD", quantity: 2m);
            await db.SeedPositionAsync(Guid.NewGuid(), userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                currency: "EUR", quantity: 100m);
        }

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithPrice(instrumentId, new InstrumentPriceLookup("USD", snapshotDate.AddDays(-1), 150m))
            .WithFxRate("USDPLN", new FxRateLookup(snapshotDate, 4.00m))
            .WithFxRate("EURPLN", new FxRateLookup(snapshotDate, 4.30m));

        await RunConsumerAsync(priceQuoteClient, async provider =>
        {
            await PublishSyncAsync(provider, snapshotDate, cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            await using var db = OpenDbContext(containers);

            var price = await db.Set<LatestInstrumentPrice>().SingleAsync(p => p.InstrumentId == instrumentId, cancellationToken);
            Assert.Equal("USD", price.QuoteCurrency);
            Assert.Equal(snapshotDate.AddDays(-1), price.Date);
            Assert.Equal(150m, price.Close);

            var usd = await db.Set<LatestFxRate>().SingleAsync(r => r.Pair == "USDPLN", cancellationToken);
            Assert.Equal(snapshotDate, usd.Date);
            Assert.Equal(4.00m, usd.Rate);

            var eur = await db.Set<LatestFxRate>().SingleAsync(r => r.Pair == "EURPLN", cancellationToken);
            Assert.Equal(snapshotDate, eur.Date);
            Assert.Equal(4.30m, eur.Rate);
        }, cancellationToken);
    }

    [Fact]
    public async Task Consume_OlderQuoteDoesNotOverwriteNewerLocalRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var snapshotDate = new DateOnly(2026, 8, 10);
        var newerDate = snapshotDate.AddDays(2);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var instrumentId = Guid.NewGuid();

        await using (var db = OpenDbContext(containers))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userId, portfolioId, cancellationToken,
                assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market,
                instrumentId: instrumentId, currency: "USD", quantity: 2m);

            await db.SeedLatestInstrumentPriceAsync(instrumentId, "USD", newerDate, 160m, cancellationToken);
            await db.SeedLatestFxRateAsync("USDPLN", newerDate, 4.10m, cancellationToken);
        }

        var priceQuoteClient = new FakePriceQuoteClient()
            .WithPrice(instrumentId, new InstrumentPriceLookup("USD", snapshotDate, 150m))
            .WithFxRate("USDPLN", new FxRateLookup(snapshotDate, 4.00m));

        await RunConsumerAsync(priceQuoteClient, async provider =>
        {
            await PublishSyncAsync(provider, snapshotDate, cancellationToken);
            await WaitForSnapshotAsync(provider, portfolioId, snapshotDate, cancellationToken);

            await using var db = OpenDbContext(containers);

            var price = await db.Set<LatestInstrumentPrice>().SingleAsync(p => p.InstrumentId == instrumentId, cancellationToken);
            Assert.Equal(newerDate, price.Date);
            Assert.Equal(160m, price.Close);

            var usd = await db.Set<LatestFxRate>().SingleAsync(r => r.Pair == "USDPLN", cancellationToken);
            Assert.Equal(newerDate, usd.Date);
            Assert.Equal(4.10m, usd.Rate);
        }, cancellationToken);
    }

    private static async Task PublishSyncAsync(ServiceProvider provider, DateOnly snapshotDate, CancellationToken cancellationToken)
    {
        var bus = provider.GetRequiredService<IBus>();
        await bus.Publish(new DailyPricesSynced
        {
            RunId = Guid.NewGuid(),
            SyncDate = snapshotDate,
            SyncedCount = 1,
            FailedCount = 0,
            NoDataCount = 0,
        }, cancellationToken);
    }

    private Task RunConsumerAsync(
        IPriceQuoteClient priceQuoteClient, Func<ServiceProvider, Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            containers,
            x => x.AddConsumer<DailyPricesSyncedConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken,
            priceQuoteClient);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<DailyPricesSyncedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
