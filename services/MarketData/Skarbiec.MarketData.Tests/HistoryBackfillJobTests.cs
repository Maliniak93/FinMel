using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// HistoryBackfillJob's business logic (T2.7 AC: ≥1 year backfilled, idempotent re-run; spec-04
/// design decision 6: FX backfill moved out to <see cref="FxSyncJob"/> entirely, so this job no
/// longer depends on <see cref="IFxRateSource"/> at all) exercised via
/// <see cref="HistoryBackfillJob.RunAsync"/> directly — no Quartz scheduler involved, mirroring
/// <see cref="PriceSyncJobTests"/>. Enqueuing mechanics are covered separately in
/// <see cref="HistoryBackfillSchedulingTests"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class HistoryBackfillJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly OneYearAgo = Today.AddDays(-365);

    /// <summary>spec-04 AC19: a USD (non-PLN) instrument's backfill still writes only its own quote
    /// history — no <see cref="FxRate"/> row, which is now FxSyncJob's job alone (design decision 6).</summary>
    [Fact]
    public async Task RunAsync_UsdInstrument_BackfillsOneYearOfQuotes_AndWritesNoFxRates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = OneYearOfDates().Select(d => new InstrumentQuote(instrument.Id, d, 100m)).ToList();
        var source = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var storedQuotes = await db.PriceQuotes.Where(q => q.InstrumentId == instrument.Id).ToListAsync(cancellationToken);
        Assert.Equal(quotes.Count, storedQuotes.Count);
        Assert.True(storedQuotes.Min(q => q.Date) <= OneYearAgo);

        Assert.Equal(0, await db.FxRates.CountAsync(cancellationToken));
    }

    /// <summary>spec-04 AC19: every HistoryBackfillJob run — including a PLN instrument's, which never
    /// needed FX in the first place — writes one <see cref="SyncRun"/> with <c>Kind = Backfill</c> so
    /// all three jobs share one run log (design decision 7).</summary>
    [Fact]
    public async Task RunAsync_WritesSyncRunWithKindBackfill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("XAU", PriceSource.Nbp, "PLN", AssetClass.PreciousMetal);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = OneYearOfDates().Select(d => new InstrumentQuote(instrument.Id, d, 350m)).ToList();
        var source = new ScriptedPriceSource(PriceSource.Nbp, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunKind.Backfill, run.Kind);
    }

    [Fact]
    public async Task RunAsync_CalledTwice_UpsertsInsteadOfDuplicating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var dates = OneYearOfDates();

        var firstQuotes = dates.Select(d => new InstrumentQuote(instrument.Id, d, 100m)).ToList();
        var firstSource = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(firstQuotes));
        var firstJob = new HistoryBackfillJob(db, [firstSource], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await firstJob.RunAsync(instrument.Id, cancellationToken);

        var secondQuotes = dates.Select(d => new InstrumentQuote(instrument.Id, d, 105m)).ToList();
        var secondSource = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(secondQuotes));
        var secondJob = new HistoryBackfillJob(db, [secondSource], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await secondJob.RunAsync(instrument.Id, cancellationToken);

        Assert.Equal(dates.Count, await db.PriceQuotes.CountAsync(q => q.InstrumentId == instrument.Id, cancellationToken));

        var latestDate = dates[^1];
        var latestQuote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id && q.Date == latestDate, cancellationToken);
        Assert.Equal(105m, latestQuote.Close);
    }

    private static List<DateOnly> OneYearOfDates() =>
        Enumerable.Range(0, 366).Select(offset => OneYearAgo.AddDays(offset)).ToList();

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
