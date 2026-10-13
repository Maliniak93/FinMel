using System.Collections.Concurrent;
using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.GoldApi;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.MarketData.Tests;

// Each fact builds a real outbox-aware IPublishEndpoint through HostlessOutboxProvider, on the DbContext the job runs against.
[Collection(TestingDefaults.SerialCollectionName)]
public sealed class PriceSyncJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private static readonly DateOnly Today = new(2026, 8, 3);

    [Fact]
    public async Task RunAsync_OneSourceFails_OtherSynced_RunRecordedAsPartial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var goldInstrument = NewUsedInstrument(db, "XAU", PriceSource.GoldApi, "USD", AssetClass.PreciousMetal);
        var yahooInstrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);

        IPriceSource[] sources =
        [
            new ScriptedPriceSource(PriceSource.GoldApi, PriceFetchResult<InstrumentQuote>.Success(
                [new InstrumentQuote(goldInstrument.Id, Today, 350.12m)])),
            // One source errors out entirely; the other must still sync.
            new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Error("yahoo is down")),
        ];

        var job = new PriceSyncJob(db, sources, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Partial, run.Status);
        Assert.Equal(1, run.SyncedCount);
        Assert.Equal(1, run.FailedCount);
        Assert.Equal(0, run.NoDataCount);
        Assert.NotNull(run.FinishedAt);

        var quotes = await db.PriceQuotes.ToListAsync(cancellationToken);
        Assert.Single(quotes);
        Assert.Contains(quotes, q => q.InstrumentId == goldInstrument.Id && q.Close == 350.12m);
        Assert.DoesNotContain(quotes, q => q.InstrumentId == yahooInstrument.Id);
    }

    [Fact]
    public async Task RunAsync_UsedMetal_StoresTodaysGramQuoteFromGoldApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var gold = NewUsedInstrument(db, "XAU", PriceSource.GoldApi, "USD", AssetClass.PreciousMetal);
        await db.SaveChangesAsync(cancellationToken);

        var client = new FakeGoldApiClient().WithPricePerOunce("XAU", 4165.5m, DateTimeOffset.UtcNow);
        var source = new GoldApiPriceSource(client, NullLogger<GoldApiPriceSource>.Instance);

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var quote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == gold.Id, cancellationToken);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), quote.Date);
        Assert.Equal(Math.Round(4165.5m / 31.1034768m, 8), quote.Close);
    }

    [Fact]
    public async Task RunAsync_CalledTwiceForSameDay_UpsertsInsteadOfDuplicating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);

        var firstRunSource = new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 100m)]));
        var firstJob = new PriceSyncJob(db, [firstRunSource], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await firstJob.RunAsync(cancellationToken);

        var secondRunSource = new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 105m)]));
        var secondJob = new PriceSyncJob(db, [secondRunSource], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await secondJob.RunAsync(cancellationToken);

        var quote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id && q.Date == Today, cancellationToken);
        Assert.Equal(105m, quote.Close);

        Assert.Equal(2, await db.SyncRuns.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task RunAsync_SyncsOnlyInstrumentsInUse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var activities = new ConcurrentBag<Activity>();
        using var listener = ListenToPriceSyncJob(activities);

        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var usedInstrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "PLN", AssetClass.Stock);
        var unusedInstrument = NewInstrument("MSFT.US", PriceSource.Yahoo, "PLN", AssetClass.Stock);
        db.Instruments.Add(unusedInstrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(usedInstrument.Id, Today, 190m)]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        Assert.True(await db.PriceQuotes.AnyAsync(q => q.InstrumentId == usedInstrument.Id, cancellationToken));
        Assert.False(await db.PriceQuotes.AnyAsync(q => q.InstrumentId == unusedInstrument.Id, cancellationToken));

        // The scripted source ignores its input, so the fetched ids and the counters, not the quote rows, show the skip.
        Assert.Equal(usedInstrument.Id, Assert.Single(source.LatestFetchedInstrumentIds));

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Completed, run.Status);
        Assert.Equal(1, run.SyncedCount);
        Assert.Equal(0, run.FailedCount);
        Assert.Equal(0, run.NoDataCount);

        var jobActivity = Assert.Single(
            activities, a => a.OperationName == "PriceSyncJob.Run" && Equals(a.GetTagItem("skarbiec.sync_run.id"), run.Id));
        Assert.Equal(1, Assert.IsType<int>(jobActivity.GetTagItem("skarbiec.sync_run.skipped")));
    }

    [Fact]
    public async Task RunAsync_SyncsWatchedInstruments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var activities = new ConcurrentBag<Activity>();
        using var listener = ListenToPriceSyncJob(activities);

        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var heldInstrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "PLN", AssetClass.Stock);
        var watchedInstrument = NewWatchedInstrument(db, "MSFT.US", PriceSource.Yahoo, "PLN", AssetClass.Stock);
        var unseenInstrument = NewInstrument("GOOG.US", PriceSource.Yahoo, "PLN", AssetClass.Stock);
        db.Instruments.Add(unseenInstrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Success(
        [
            new InstrumentQuote(heldInstrument.Id, Today, 190m),
            new InstrumentQuote(watchedInstrument.Id, Today, 400m),
        ]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        Assert.Equal(2, source.LatestFetchedInstrumentIds.Count);
        Assert.Contains(heldInstrument.Id, source.LatestFetchedInstrumentIds);
        Assert.Contains(watchedInstrument.Id, source.LatestFetchedInstrumentIds);
        Assert.DoesNotContain(unseenInstrument.Id, source.LatestFetchedInstrumentIds);

        Assert.True(await db.PriceQuotes.AnyAsync(q => q.InstrumentId == watchedInstrument.Id, cancellationToken));
        Assert.False(await db.PriceQuotes.AnyAsync(q => q.InstrumentId == unseenInstrument.Id, cancellationToken));

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Completed, run.Status);
        Assert.Equal(2, run.SyncedCount);

        var jobActivity = Assert.Single(
            activities, a => a.OperationName == "PriceSyncJob.Run" && Equals(a.GetTagItem("skarbiec.sync_run.id"), run.Id));
        Assert.Equal(1, Assert.IsType<int>(jobActivity.GetTagItem("skarbiec.sync_run.skipped")));
    }

    private static ActivityListener ListenToPriceSyncJob(ConcurrentBag<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PriceSyncJob.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    // The job syncs only instruments in use, so every instrument a fact expects synced needs a usage row.
    private static Instrument NewUsedInstrument(
        MarketDataDbContext db, string ticker, PriceSource source, string quoteCurrency, AssetClass assetClass)
    {
        var instrument = NewInstrument(ticker, source, quoteCurrency, assetClass);
        db.Instruments.Add(instrument);
        db.InstrumentUsages.Add(new InstrumentUsage
        {
            InstrumentId = instrument.Id,
            AssetCount = 1,
            FirstUsedAt = DateTimeOffset.UtcNow,
        });
        return instrument;
    }

    // A watched instrument is in use without any holding: it has a usage row with WatchCount set and AssetCount 0.
    private static Instrument NewWatchedInstrument(
        MarketDataDbContext db, string ticker, PriceSource source, string quoteCurrency, AssetClass assetClass)
    {
        var instrument = NewInstrument(ticker, source, quoteCurrency, assetClass);
        db.Instruments.Add(instrument);
        db.InstrumentUsages.Add(new InstrumentUsage
        {
            InstrumentId = instrument.Id,
            AssetCount = 0,
            WatchCount = 1,
            FirstUsedAt = DateTimeOffset.UtcNow,
        });
        return instrument;
    }

    private static Instrument NewInstrument(string ticker, PriceSource source, string quoteCurrency, AssetClass assetClass) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        Name = ticker,
        Source = source,
        QuoteCurrency = quoteCurrency,
        AssetClass = assetClass,
    };
}
