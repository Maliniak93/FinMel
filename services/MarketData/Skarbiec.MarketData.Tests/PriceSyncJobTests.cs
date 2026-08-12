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
/// idempotency) exercised via <see cref="PriceSyncJob.RunAsync"/> directly — no Quartz scheduler
/// involved. Scheduling mechanics (cron firing, restart/cluster safety) are covered separately in
/// <see cref="PriceSyncSchedulingTests"/>. Outbox atomicity (T2.10 AC) is covered in
/// <see cref="MarketDataOutboxTests"/>; the job still needs a real, outbox-aware
/// <see cref="IPublishEndpoint"/> here (a hand-rolled no-op fake would have to match the whole
/// interface), so each fact builds one via <see cref="HostlessOutboxProvider"/> — same helper, same
/// scope, same DbContext instance the job runs against.
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

        var job = new PriceSyncJob(db, sources, fxSource, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
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

    /// <summary>M1.4 AC: even with no EUR-quoted instrument in the dictionary, the daily run still
    /// writes a current EURPLN row — before this, <c>PriceSyncJob</c> only requested FX for currencies
    /// an instrument happened to quote in, so EUR (nothing quotes in it) never synced at all and a
    /// currency-valued EUR asset (M1.4's third mode) would have valued off <c>MarketDataSeeder</c>'s
    /// 2020 bootstrap rate forever.</summary>
    [Fact]
    public async Task RunAsync_NoEurQuotedInstrument_StillWritesCurrentEurPlnRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        IPriceSource[] sources =
        [
            new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
                [new InstrumentQuote(instrument.Id, Today, 190m)])),
        ];
        // NBP table A returns every published currency in one call regardless of what's asked for
        // (M1.4) — USD (instrument-backed) and EUR (supported-set-only, no instrument here) both come back.
        var fxSource = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success(
            [new FxRateQuote("USDPLN", Today, 3.65m), new FxRateQuote("EURPLN", Today, 4.30m)]));

        var job = new PriceSyncJob(db, sources, fxSource, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var eurRate = await db.FxRates.SingleAsync(r => r.Pair == "EURPLN" && r.Date == Today, cancellationToken);
        Assert.Equal(4.30m, eurRate.Rate);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Completed, run.Status);
        Assert.Equal(0, run.FailedCount);
    }

    /// <summary>M1.4's FailedCount decision: a supported-set currency nobody currently holds an
    /// instrument in (EUR here) isn't penalized just because it's missing from NBP's response — but a
    /// currency an instrument actually depends on (USD here) still is, exactly as before M1.4.</summary>
    [Fact]
    public async Task RunAsync_FxResponseMissingSupportedOnlyCurrency_NotFailed_ButMissingInstrumentBackedCurrencyIs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        IPriceSource[] sources =
        [
            new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
                [new InstrumentQuote(instrument.Id, Today, 190m)])),
        ];
        // USD (instrument-backed) is missing from the response; EUR (supported-set-only) is present.
        var fxSource = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success(
            [new FxRateQuote("EURPLN", Today, 4.30m)]));

        var job = new PriceSyncJob(db, sources, fxSource, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        // 1 instrument quote + 1 EUR rate synced; 1 failure — the missing USDPLN (instrument-backed).
        // EUR being requested-but-quoted-by-nobody never enters the failure count at all.
        Assert.Equal(2, run.SyncedCount);
        Assert.Equal(1, run.FailedCount);
        Assert.Equal(SyncRunStatus.Partial, run.Status);
    }

    [Fact]
    public async Task RunAsync_CalledTwiceForSameDay_UpsertsInsteadOfDuplicating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = NewInstrument("AAPL.US", PriceSource.Stooq, "USD", AssetClass.Stock);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var noFx = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success(
            [new FxRateQuote("USDPLN", Today, 3.60m)]));

        var firstRunSource = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 100m)]));
        var firstJob = new PriceSyncJob(db, [firstRunSource], noFx, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await firstJob.RunAsync(cancellationToken);

        var secondRunSource = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 105m)]));
        var secondJob = new PriceSyncJob(db, [secondRunSource], noFx, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
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
