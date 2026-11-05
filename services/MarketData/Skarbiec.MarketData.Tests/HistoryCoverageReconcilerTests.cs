using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class HistoryCoverageReconcilerTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task LaggingCurrency_EnqueuesFxJob()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await db.SeedAssetLinkAsync(Today.AddDays(-800), cancellationToken);

        var (reconciler, _, fxTrigger) = NewReconciler(db);
        await reconciler.ReconcileAsync(cancellationToken);

        Assert.Equal(1, fxTrigger.Attempts);
    }

    [Fact]
    public async Task NothingLagging_EnqueuesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        await db.SeedCurrencyCatalogAsync(cancellationToken, historyCoveredFrom: Today.AddDays(-1000));
        await db.SeedAssetLinkAsync(Today.AddDays(-800), cancellationToken);

        var (reconciler, backfillTrigger, fxTrigger) = NewReconciler(db);
        await reconciler.ReconcileAsync(cancellationToken);

        Assert.Equal(0, fxTrigger.Attempts);
        Assert.Empty(backfillTrigger.Enqueued);
    }

    [Fact]
    public async Task InstrumentBehindCoverage_EnqueuesBackfill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var behindId = await db.SeedInstrumentAsync("AAPL.US", "Apple", PriceSource.Yahoo, "USD", cancellationToken);
        var neverCoveredId = await db.SeedInstrumentAsync("MSFT.US", "Microsoft", PriceSource.Yahoo, "USD", cancellationToken);
        var coveredId = await db.SeedInstrumentAsync("GC=F", "Gold", PriceSource.Yahoo, "USD", cancellationToken);
        db.Instruments.Single(i => i.Id == behindId).HistoryCoveredFrom = new DateOnly(2024, 1, 1);
        db.Instruments.Single(i => i.Id == coveredId).HistoryCoveredFrom = new DateOnly(2023, 1, 1);
        await db.SaveChangesAsync(cancellationToken);

        await db.SeedAssetLinkAsync(new DateOnly(2023, 9, 1), cancellationToken, instrumentId: behindId);
        await db.SeedAssetLinkAsync(new DateOnly(2023, 6, 1), cancellationToken, instrumentId: behindId);
        await db.SeedAssetLinkAsync(new DateOnly(2024, 5, 1), cancellationToken, instrumentId: neverCoveredId);
        await db.SeedAssetLinkAsync(new DateOnly(2023, 6, 1), cancellationToken, instrumentId: coveredId);

        var (reconciler, backfillTrigger, _) = NewReconciler(db);
        await reconciler.ReconcileAsync(cancellationToken);

        Assert.Equal([new DateOnly(2023, 6, 1)], backfillTrigger.FromsFor(behindId));
        Assert.Equal([new DateOnly(2024, 5, 1)], backfillTrigger.FromsFor(neverCoveredId));
        Assert.Equal(0, backfillTrigger.CountFor(coveredId));
    }

    [Fact]
    public async Task InstrumentWithoutLiveLink_IsIgnored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var unusedId = await db.SeedInstrumentAsync("AAPL.US", "Apple", PriceSource.Yahoo, "USD", cancellationToken);
        var removedId = await db.SeedInstrumentAsync("MSFT.US", "Microsoft", PriceSource.Yahoo, "USD", cancellationToken);
        await db.SeedAssetLinkAsync(new DateOnly(2023, 6, 1), cancellationToken, instrumentId: removedId, isRemoved: true);

        var (reconciler, backfillTrigger, _) = NewReconciler(db);
        await reconciler.ReconcileAsync(cancellationToken);

        Assert.Equal(0, backfillTrigger.CountFor(unusedId));
        Assert.Equal(0, backfillTrigger.CountFor(removedId));
    }

    private static (HistoryCoverageReconciler Reconciler, FakeHistoryBackfillTrigger BackfillTrigger, FakeFxHistoryBackfillTrigger FxTrigger)
        NewReconciler(MarketDataDbContext db)
    {
        var backfillTrigger = new FakeHistoryBackfillTrigger();
        var fxTrigger = new FakeFxHistoryBackfillTrigger();
        var reconciler = new HistoryCoverageReconciler(
            db, backfillTrigger, fxTrigger, TimeProvider.System, NullLogger<HistoryCoverageReconciler>.Instance);
        return (reconciler, backfillTrigger, fxTrigger);
    }
}
