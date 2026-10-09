using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.GoldApi;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

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

        var instrument = NewInstrument("AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = OneYearOfDates().Select(d => new InstrumentQuote(instrument.Id, d, 100m)).ToList();
        var source = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, OneYearAgo, cancellationToken);

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
        await job.RunAsync(instrument.Id, OneYearAgo, cancellationToken);

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
        await job.RunAsync(instrument.Id, OneYearAgo, cancellationToken);

        var quote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id, cancellationToken);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), quote.Date);
        Assert.Equal(Math.Round(62.2m / 31.1034768m, 8), quote.Close);
    }

    [Fact]
    public async Task RunAsync_CalledTwice_UpsertsInsteadOfDuplicating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var dates = OneYearOfDates();

        var firstQuotes = dates.Select(d => new InstrumentQuote(instrument.Id, d, 100m)).ToList();
        var firstSource = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(firstQuotes));
        var firstJob = new HistoryBackfillJob(db, [firstSource], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await firstJob.RunAsync(instrument.Id, OneYearAgo, cancellationToken);

        // As if two enqueues for the same window raced, so the second run still fetches the stretch the first one stored.
        await db.Instruments.Where(i => i.Id == instrument.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.HistoryCoveredFrom, (DateOnly?)null), cancellationToken);

        var secondQuotes = dates.Select(d => new InstrumentQuote(instrument.Id, d, 105m)).ToList();
        var secondSource = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(secondQuotes));
        var secondJob = new HistoryBackfillJob(db, [secondSource], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await secondJob.RunAsync(instrument.Id, OneYearAgo, cancellationToken);

        Assert.Equal(dates.Count, await db.PriceQuotes.CountAsync(q => q.InstrumentId == instrument.Id, cancellationToken));

        var latestDate = dates[^1];
        var latestQuote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id && q.Date == latestDate, cancellationToken);
        Assert.Equal(105m, latestQuote.Close);
    }

    [Fact]
    public async Task ExtendsWindowBackwards()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        instrument.HistoryCoveredFrom = new DateOnly(2024, 1, 1);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var from = new DateOnly(2023, 6, 1);
        var quotes = new[] { from, new DateOnly(2023, 6, 2), new DateOnly(2023, 12, 31) }
            .Select(d => new InstrumentQuote(instrument.Id, d, 100m)).ToList();
        var source = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, from, cancellationToken);

        Assert.Equal([(from, new DateOnly(2023, 12, 31))], source.HistoryWindows);
        Assert.Equal(quotes.Count, await db.PriceQuotes.CountAsync(q => q.InstrumentId == instrument.Id, cancellationToken));

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(from, stored.HistoryCoveredFrom);
    }

    [Fact]
    public async Task Error_KeepsCoveredFrom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("AAPL.US", PriceSource.Yahoo, "USD", AssetClass.Stock);
        instrument.HistoryCoveredFrom = new DateOnly(2024, 1, 1);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Error("transient failure"));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, new DateOnly(2023, 6, 1), cancellationToken);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(new DateOnly(2024, 1, 1), stored.HistoryCoveredFrom);
    }

    [Fact]
    public async Task ClampedSource_RecordsRequestedFrom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("bitcoin", PriceSource.CoinGecko, "USD", AssetClass.Crypto);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = new[] { new InstrumentQuote(instrument.Id, Today.AddDays(-365), 100m) };
        var source = new ScriptedPriceSource(
            PriceSource.CoinGecko, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes), maxHistoryDays: 365);

        var twoYearsAgo = Today.AddDays(-730);
        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, twoYearsAgo, cancellationToken);

        var window = Assert.Single(source.HistoryWindows);
        Assert.Equal(Today.AddDays(-365), window.From);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(twoYearsAgo, stored.HistoryCoveredFrom);
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
