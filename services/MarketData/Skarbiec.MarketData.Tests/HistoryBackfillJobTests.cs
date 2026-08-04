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
/// HistoryBackfillJob's business logic (T2.7 AC: ≥1 year backfilled, FX backfilled alongside a
/// non-PLN instrument, idempotent re-run) exercised via <see cref="HistoryBackfillJob.RunAsync"/>
/// directly — no Quartz scheduler involved, mirroring <see cref="PriceSyncJobTests"/>. Enqueuing
/// mechanics (returns before any fetch happens, a scheduled run actually lands the data) are covered
/// separately in <see cref="HistoryBackfillSchedulingTests"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class HistoryBackfillJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly OneYearAgo = Today.AddDays(-365);

    [Fact]
    public async Task RunAsync_UsdInstrument_BackfillsOneYearOfQuotesAndItsFxPair()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = OneYearOfDates().Select(d => new InstrumentQuote(instrument.Id, d, 100m)).ToList();
        var fxRates = OneYearOfDates().Select(d => new FxRateQuote("USDPLN", d, 4m)).ToList();

        var source = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));
        var fxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.Success(fxRates));

        var job = new HistoryBackfillJob(db, [source], fxSource, TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var storedQuotes = await db.PriceQuotes.Where(q => q.InstrumentId == instrument.Id).ToListAsync(cancellationToken);
        Assert.Equal(quotes.Count, storedQuotes.Count);
        Assert.True(storedQuotes.Min(q => q.Date) <= OneYearAgo);

        var storedFxRates = await db.FxRates.Where(r => r.Pair == "USDPLN").ToListAsync(cancellationToken);
        Assert.Equal(fxRates.Count, storedFxRates.Count);
        Assert.True(storedFxRates.Min(r => r.Date) <= OneYearAgo);
    }

    [Fact]
    public async Task RunAsync_PlnInstrument_DoesNotBackfillFx()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("XAU", PriceSource.Nbp, "PLN", AssetClass.PreciousMetal);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = OneYearOfDates().Select(d => new InstrumentQuote(instrument.Id, d, 350m)).ToList();
        var source = new ScriptedPriceSource(PriceSource.Nbp, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));
        // No historyResult scripted: FetchHistoryAsync throws if called — proves a PLN instrument
        // never triggers an FX backfill (there's nothing to convert).
        var fxSource = new ScriptedFxRateSource();

        var job = new HistoryBackfillJob(db, [source], fxSource, TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        Assert.Equal(0, fxSource.HistoryFetchCount);
        Assert.Equal(quotes.Count, await db.PriceQuotes.CountAsync(q => q.InstrumentId == instrument.Id, cancellationToken));
        Assert.Equal(0, await db.FxRates.CountAsync(cancellationToken));
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
        var firstFx = dates.Select(d => new FxRateQuote("USDPLN", d, 4m)).ToList();
        var firstSource = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(firstQuotes));
        var firstFxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.Success(firstFx));
        var firstJob = new HistoryBackfillJob(db, [firstSource], firstFxSource, TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await firstJob.RunAsync(instrument.Id, cancellationToken);

        var secondQuotes = dates.Select(d => new InstrumentQuote(instrument.Id, d, 105m)).ToList();
        var secondFx = dates.Select(d => new FxRateQuote("USDPLN", d, 4.10m)).ToList();
        var secondSource = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(secondQuotes));
        var secondFxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.Success(secondFx));
        var secondJob = new HistoryBackfillJob(db, [secondSource], secondFxSource, TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await secondJob.RunAsync(instrument.Id, cancellationToken);

        Assert.Equal(dates.Count, await db.PriceQuotes.CountAsync(q => q.InstrumentId == instrument.Id, cancellationToken));
        Assert.Equal(dates.Count, await db.FxRates.CountAsync(r => r.Pair == "USDPLN", cancellationToken));

        var latestDate = dates[^1];
        var latestQuote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id && q.Date == latestDate, cancellationToken);
        Assert.Equal(105m, latestQuote.Close);

        var latestFx = await db.FxRates.SingleAsync(r => r.Pair == "USDPLN" && r.Date == latestDate, cancellationToken);
        Assert.Equal(4.10m, latestFx.Rate);
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
