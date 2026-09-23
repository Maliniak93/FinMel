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
/// <c>AssetPositionChanged</c> in, <see cref="Position"/> upserted by <c>AssetId</c> — idempotently
/// and never regressed by an out-of-order redelivery (spec-03 AC1-4) — and, since spec-07, today's
/// <see cref="AssetValuation"/> lines and <see cref="ValuationSnapshot"/> of the event's portfolio
/// revalued from Reporting's locally stored last prices and FX rates (spec-07 AC1-7). Builds its own
/// provider (no HTTP host needed) on a queue name unique to this test class.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AssetPositionChangedConsumerTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string QueueName = "asset-position-changed-consumer-test";

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
                ManualValueAmount = null,
                ManualValueDate = null,
                PortfolioIsArchived = false,
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

    /// <summary>spec-03 AC3, extended by spec-07 AC5: a dropped stale-version event must not touch today's valuation either.</summary>
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

            // No signal to wait on for "it was ignored" — give a genuine (mis-ordered) delivery time
            // to land before asserting the stored row was left untouched.
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

    /// <summary>spec-03 AC4, extended by spec-07 AC6: a redelivery leaves exactly one line per asset and one snapshot for today.</summary>
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

            // Redeliver: same MessageId, same content. The inbox (InboxState, keyed on MessageId +
            // ConsumerId) must recognize it and skip the consumer body entirely.
            await bus.Publish(@event, ctx => ctx.MessageId = messageId, cancellationToken);

            // No signal to wait on for "it was skipped" — give a genuine redelivery time to land
            // before asserting it never did.
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

    /// <summary>spec-07 AC1.</summary>
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

    /// <summary>spec-07 AC2: a foreign currency is converted with the locally stored last FX rate, never a REST call.</summary>
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

    /// <summary>spec-07 AC3 / design decision 3: no local price means a zero-valued stale line, and the snapshot turns stale.</summary>
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
                PortfolioIsArchived = false,
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

    /// <summary>spec-07 AC4: only the event's portfolio is revalued — another portfolio's today rows stay exactly as they were, even when they disagree with its positions.</summary>
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

            // B's position says 5 000 PLN but its rows for today say 777: any revaluation of B would
            // be visible as 5 000.
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

    /// <summary>
    /// spec-07 AC7: an archived portfolio's last snapshot stays put. A second, non-archived portfolio
    /// consumed afterwards on the same queue is the positive control — it proves the revaluation
    /// path is live, so "no rows" for the archived one means "skipped", not "never implemented".
    /// </summary>
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

            // The position upsert and any revaluation commit in one transaction, so once the
            // position is visible the whole consume has finished.
            await WaitForPositionAsync(provider, archivedAssetId, cancellationToken);

            await bus.Publish(Event(controlAssetId, controlPortfolioId, userId, quantity: 50m, version: 0), cancellationToken);
            await WaitForSnapshotAsync(provider, controlPortfolioId, today, cancellationToken);

            Assert.Null(await GetSnapshotAsync(containers, archivedPortfolioId, today, cancellationToken));
            Assert.Empty(await GetLinesAsync(containers, archivedPortfolioId, today, cancellationToken));
        }, cancellationToken);
    }

    private static AssetPositionChanged Event(
        Guid assetId,
        Guid portfolioId,
        Guid userId,
        decimal quantity,
        long version,
        string currency = "PLN",
        bool portfolioIsArchived = false) => new()
        {
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
            x => x.AddConsumer<AssetPositionChangedConsumer>(typeof(TestConsumerDefinition)),
            action,
            cancellationToken);

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
