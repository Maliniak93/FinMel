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

/// <summary>
/// PriceSyncJob's business logic (T2.6 AC: per-source isolation, partial-run recording, upsert
/// idempotency; spec-04 design decision 6: currencies/FX dropped entirely — <see cref="FxSyncJob"/>
/// (<see cref="FxSyncJobTests"/>) covers every catalog currency unconditionally now, so this job no
/// longer depends on <see cref="IFxRateSource"/> at all) exercised via
/// <see cref="PriceSyncJob.RunAsync"/> directly — no Quartz scheduler involved. Scheduling mechanics
/// (cron firing, restart/cluster safety) are covered separately in <see cref="PriceSyncSchedulingTests"/>.
/// Outbox atomicity (T2.10 AC) is covered in <see cref="MarketDataOutboxTests"/>; the job still needs
/// a real, outbox-aware <see cref="IPublishEndpoint"/> here (a hand-rolled no-op fake would have to
/// match the whole interface), so each fact builds one via <see cref="HostlessOutboxProvider"/> — same
/// helper, same scope, same DbContext instance the job runs against.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PriceSyncJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // Explicit field: the primary constructor parameter is also passed to the base constructor
    // above, so referencing it directly elsewhere in this class would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private static readonly DateOnly Today = new(2026, 8, 3);

    [Fact]
    public async Task RunAsync_MiddleSourceFails_OtherTwoSynced_RunRecordedAsPartial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var nbpInstrument = NewUsedInstrument(db, "XAU", PriceSource.Nbp, "PLN", AssetClass.PreciousMetal);
        var stooqInstrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        var coinGeckoInstrument = NewUsedInstrument(db, "bitcoin", PriceSource.CoinGecko, "USD", AssetClass.Crypto);
        await db.SaveChangesAsync(cancellationToken);

        IPriceSource[] sources =
        [
            new ScriptedPriceSource(PriceSource.Nbp, PriceFetchResult<InstrumentQuote>.Success(
                [new InstrumentQuote(nbpInstrument.Id, Today, 350.12m)])),
            // The "middle" source — its whole fetch errors out, the other two must still sync.
            new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Error("stooq is down")),
            new ScriptedPriceSource(PriceSource.CoinGecko, PriceFetchResult<InstrumentQuote>.Success(
                [new InstrumentQuote(coinGeckoInstrument.Id, Today, 65_000m)])),
        ];

        var job = new PriceSyncJob(db, sources, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Partial, run.Status);
        Assert.Equal(2, run.SyncedCount); // Nbp instrument + CoinGecko instrument
        Assert.Equal(1, run.FailedCount); // Stooq instrument
        Assert.Equal(0, run.NoDataCount);
        Assert.NotNull(run.FinishedAt);

        var quotes = await db.PriceQuotes.ToListAsync(cancellationToken);
        Assert.Equal(2, quotes.Count);
        Assert.Contains(quotes, q => q.InstrumentId == nbpInstrument.Id && q.Close == 350.12m);
        Assert.Contains(quotes, q => q.InstrumentId == coinGeckoInstrument.Id && q.Close == 65_000m);
        Assert.DoesNotContain(quotes, q => q.InstrumentId == stooqInstrument.Id);
    }

    [Fact]
    public async Task RunAsync_CalledTwiceForSameDay_UpsertsInsteadOfDuplicating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        await db.SaveChangesAsync(cancellationToken);

        var firstRunSource = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 100m)]));
        var firstJob = new PriceSyncJob(db, [firstRunSource], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await firstJob.RunAsync(cancellationToken);

        var secondRunSource = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 105m)]));
        var secondJob = new PriceSyncJob(db, [secondRunSource], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await secondJob.RunAsync(cancellationToken);

        var quote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id && q.Date == Today, cancellationToken);
        Assert.Equal(105m, quote.Close);

        Assert.Equal(2, await db.SyncRuns.CountAsync(cancellationToken));
    }

    /// <summary>spec-04 AC17: PriceSyncJob.GetInstrumentsToSyncAsync joins InstrumentUsage and keeps
    /// only AssetCount > 0 — the in-use instrument gets fetched, the unused one is skipped
    /// entirely (never handed to the source, not counted failed) and reported as the
    /// <c>skarbiec.sync_run.skipped</c> activity tag.</summary>
    [Fact]
    public async Task RunAsync_SyncsOnlyInstrumentsInUse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PriceSyncJob.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var usedInstrument = NewUsedInstrument(db, "AAPL.US", PriceSource.Stooq, "PLN", AssetClass.Stock);
        var unusedInstrument = NewInstrument("MSFT.US", PriceSource.Stooq, "PLN", AssetClass.Stock);
        db.Instruments.Add(unusedInstrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(usedInstrument.Id, Today, 190m)]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        Assert.True(await db.PriceQuotes.AnyAsync(q => q.InstrumentId == usedInstrument.Id, cancellationToken));
        Assert.False(await db.PriceQuotes.AnyAsync(q => q.InstrumentId == unusedInstrument.Id, cancellationToken));

        // The scripted source ignores its input, so the quote rows alone can't tell "skipped" from
        // "asked for and missing" — the fetched ids and the counters can: without the usage filter the
        // unused instrument reaches the source and is counted failed, turning the run Partial.
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

    /// <summary>Adds an <see cref="Instrument"/> plus an <see cref="InstrumentUsage"/> row with
    /// <c>AssetCount > 0</c> — GetInstrumentsToSyncAsync (AC17) now filters on usage, so every fact in
    /// this class that expects an instrument to actually be synced needs one.</summary>
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
