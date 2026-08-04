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
/// PriceSyncJob's business logic (T2.6 AC: per-source isolation, partial-run recording, upsert
/// idempotency) exercised via <see cref="PriceSyncJob.RunAsync"/> directly — no Quartz scheduler
/// involved. Scheduling mechanics (cron firing, restart/cluster safety) are covered separately in
/// <see cref="PriceSyncSchedulingTests"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PriceSyncJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly DateOnly Today = new(2026, 8, 3);

    [Fact]
    public async Task RunAsync_MiddleSourceFails_OtherTwoSynced_RunRecordedAsPartial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var nbpInstrument = NewInstrument("XAU", PriceSource.Nbp, "PLN", AssetClass.PreciousMetal);
        var stooqInstrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        var coinGeckoInstrument = NewInstrument("bitcoin", PriceSource.CoinGecko, "USD", AssetClass.Crypto);
        db.Instruments.AddRange(nbpInstrument, stooqInstrument, coinGeckoInstrument);
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
        var fxSource = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success(
            [new FxRateQuote("USDPLN", Today, 3.65m)]));

        var job = new PriceSyncJob(db, sources, fxSource, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Partial, run.Status);
        Assert.Equal(3, run.SyncedCount); // Nbp instrument + CoinGecko instrument + USDPLN
        Assert.Equal(1, run.FailedCount); // Stooq instrument
        Assert.Equal(0, run.NoDataCount);
        Assert.NotNull(run.FinishedAt);

        var quotes = await db.PriceQuotes.ToListAsync(cancellationToken);
        Assert.Equal(2, quotes.Count);
        Assert.Contains(quotes, q => q.InstrumentId == nbpInstrument.Id && q.Close == 350.12m);
        Assert.Contains(quotes, q => q.InstrumentId == coinGeckoInstrument.Id && q.Close == 65_000m);
        Assert.DoesNotContain(quotes, q => q.InstrumentId == stooqInstrument.Id);

        var fxRate = await db.FxRates.SingleAsync(r => r.Date == Today, cancellationToken);
        Assert.Equal("USDPLN", fxRate.Pair);
        Assert.Equal(3.65m, fxRate.Rate);
    }

    [Fact]
    public async Task RunAsync_CalledTwiceForSameDay_UpsertsInsteadOfDuplicating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var noFx = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success(
            [new FxRateQuote("USDPLN", Today, 3.60m)]));

        var firstRunSource = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 100m)]));
        var firstJob = new PriceSyncJob(db, [firstRunSource], noFx, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await firstJob.RunAsync(cancellationToken);

        var secondRunSource = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 105m)]));
        var secondJob = new PriceSyncJob(db, [secondRunSource], noFx, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await secondJob.RunAsync(cancellationToken);

        var quote = await db.PriceQuotes.SingleAsync(q => q.InstrumentId == instrument.Id && q.Date == Today, cancellationToken);
        Assert.Equal(105m, quote.Close);

        var fxRate = await db.FxRates.SingleAsync(r => r.Pair == "USDPLN" && r.Date == Today, cancellationToken);
        Assert.Equal(3.60m, fxRate.Rate);

        Assert.Equal(2, await db.SyncRuns.CountAsync(cancellationToken));
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
