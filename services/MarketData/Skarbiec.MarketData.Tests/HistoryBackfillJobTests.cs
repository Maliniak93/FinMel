using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.GoldApi;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class HistoryBackfillJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly OneYearAgo = Today.AddDays(-365);

    // A USD instrument's backfill writes only its own quotes: FX history is FxSyncJob's alone.
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

    [Fact]
    public async Task RunAsync_WritesSyncRunWithKindBackfill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("XAU", PriceSource.GoldApi, "USD", AssetClass.PreciousMetal);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = OneYearOfDates().Select(d => new InstrumentQuote(instrument.Id, d, 350m)).ToList();
        var source = new ScriptedPriceSource(PriceSource.GoldApi, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunKind.Backfill, run.Kind);
    }

    [Fact]
    public async Task RunAsync_NewlyUsedMetal_BackfillsTodaysQuote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("XAG", PriceSource.GoldApi, "USD", AssetClass.PreciousMetal);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var client = new FakeGoldApiClient().WithPricePerOunce("XAG", 62.2m, DateTimeOffset.UtcNow);
        var source = new GoldApiPriceSource(client, NullLogger<GoldApiPriceSource>.Instance);

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var quote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id, cancellationToken);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), quote.Date);
        Assert.Equal(Math.Round(62.2m / 31.1034768m, 8), quote.Close);
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
