using System.Collections.Concurrent;
using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
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
    public async Task RunAsync_SourceAnswersForOneInstrumentOnly_RunRecordedAsPartial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var goldInstrument = NewUsedInstrument(db, "GC=F", PriceSource.Yahoo, "USD", AssetClass.PreciousMetal);
        var yahooInstrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);

        // The source answers for one instrument only; the other counts as failed.
        IPriceSource[] sources =
        [
            new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Success(
                [new InstrumentQuote(goldInstrument.Id, Today, 350.12m)])),
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
    public async Task RunAsync_UsedMetal_StoresTodaysOunceQuoteFromYahoo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var gold = NewUsedInstrument(db, "GC=F", PriceSource.Yahoo, "USD", AssetClass.PreciousMetal);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(gold.Id, Today, 4165.5m)]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var quote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == gold.Id, cancellationToken);
        Assert.Equal(Today, quote.Date);
        Assert.Equal(4165.5m, quote.Close);
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

    [Fact]
    public async Task StartupRun_FillsGapSinceLastQuote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = UtcToday();
        var lastQuote = today.AddDays(-5);
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);
        await db.SeedQuoteAsync(instrument.Id, lastQuote, 100m, cancellationToken);

        var source = new ScriptedPriceSource(
            PriceSource.Yahoo,
            historyResult: PriceFetchResult<InstrumentQuote>.Success(
            [
                new InstrumentQuote(instrument.Id, today.AddDays(-3), 101m),
                new InstrumentQuote(instrument.Id, today.AddDays(-2), 102m),
                new InstrumentQuote(instrument.Id, today, 103m),
            ]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken, SyncRunSource.Startup);

        Assert.Equal(1, source.HistoryFetchCount);
        Assert.Equal(lastQuote.AddDays(1), Assert.Single(source.HistoryWindows).From);

        var dates = await db.PriceQuotes.Where(q => q.InstrumentId == instrument.Id).Select(q => q.Date).OrderBy(d => d).ToListAsync(cancellationToken);
        Assert.Equal([lastQuote, today.AddDays(-3), today.AddDays(-2), today], dates);
    }

    [Fact]
    public async Task StartupRun_GapIsCappedAt30Days()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = UtcToday();
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);
        await db.SeedQuoteAsync(instrument.Id, today.AddDays(-60), 100m, cancellationToken);

        var source = new ScriptedPriceSource(
            PriceSource.Yahoo,
            historyResult: PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrument.Id, today, 103m)]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken, SyncRunSource.Startup);

        Assert.Equal(today.AddDays(-30), Assert.Single(source.HistoryWindows).From);
    }

    [Theory]
    [InlineData(SyncRunSource.Schedule)]
    [InlineData(SyncRunSource.Manual)]
    public async Task ScheduledAndManualRuns_FetchLatestOnly(SyncRunSource runSource)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = UtcToday();
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);
        await db.SeedQuoteAsync(instrument.Id, today.AddDays(-5), 100m, cancellationToken);

        var source = new ScriptedPriceSource(
            PriceSource.Yahoo,
            PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrument.Id, today, 103m)]),
            PriceFetchResult<InstrumentQuote>.Success([new InstrumentQuote(instrument.Id, today.AddDays(-3), 101m)]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken, runSource);

        Assert.Equal(0, source.HistoryFetchCount);
        Assert.Equal(instrument.Id, Assert.Single(source.LatestFetchedInstrumentIds));
    }

    [Fact]
    public async Task StartupRun_GapFetchError_MarksRunPartial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = UtcToday();
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var broken = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        var healthy = NewUsedInstrument(db, "MSFT.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);
        await db.SeedQuoteAsync(broken.Id, today.AddDays(-5), 100m, cancellationToken);
        await db.SeedQuoteAsync(healthy.Id, today.AddDays(-5), 200m, cancellationToken);

        var source = new ScriptedPriceSource(
            PriceSource.Yahoo,
            historyResultsByInstrumentId: new Dictionary<Guid, PriceFetchResult<InstrumentQuote>>
            {
                [broken.Id] = PriceFetchResult<InstrumentQuote>.Error("simulated failure"),
                [healthy.Id] = PriceFetchResult<InstrumentQuote>.Success(
                [
                    new InstrumentQuote(healthy.Id, today.AddDays(-3), 201m),
                    new InstrumentQuote(healthy.Id, today.AddDays(-2), 202m),
                ]),
            });

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken, SyncRunSource.Startup);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Partial, run.Status);
        Assert.Equal(1, run.FailedCount);
        Assert.Equal(2, await db.PriceQuotes.CountAsync(q => q.InstrumentId == healthy.Id && q.Date > today.AddDays(-5), cancellationToken));
        Assert.Equal(1, await db.PriceQuotes.CountAsync(q => q.InstrumentId == broken.Id, cancellationToken));
    }

    [Fact]
    public async Task StartupRun_GapFill_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = UtcToday();
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);
        await db.SeedQuoteAsync(instrument.Id, today.AddDays(-5), 100m, cancellationToken);

        PriceSyncJob NewJob() => new(
            db,
            [new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(
            [
                new InstrumentQuote(instrument.Id, today.AddDays(-3), 101m),
                new InstrumentQuote(instrument.Id, today.AddDays(-2), 102m),
            ]))],
            publishEndpoint,
            TimeProvider.System,
            NullLogger<PriceSyncJob>.Instance);

        await NewJob().RunAsync(cancellationToken, SyncRunSource.Startup);
        await NewJob().RunAsync(cancellationToken, SyncRunSource.Startup);

        Assert.Equal(3, await db.PriceQuotes.CountAsync(q => q.InstrumentId == instrument.Id, cancellationToken));
    }

    private static DateOnly UtcToday() => DateOnly.FromDateTime(DateTime.UtcNow);

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
    [Fact]
    public async Task RecordsSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var job = new PriceSyncJob(db, [], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);
        await job.RunAsync(cancellationToken, SyncRunSource.Startup);

        var runs = await db.SyncRuns.AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync(cancellationToken);
        Assert.Equal([SyncRunSource.Schedule, SyncRunSource.Startup], runs.Select(r => r.Source));
    }

    [Fact]
    public async Task UnexpectedException_FinishesRunFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var job = new PriceSyncJob(db, [new ThrowingPriceSource()], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await Record.ExceptionAsync(() => job.RunAsync(cancellationToken));

        await using var readDb = CreateDbContext();
        var run = await readDb.SyncRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.NotNull(run.FinishedAt);
    }

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
