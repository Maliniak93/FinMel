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
/// T2.8 AC: a custom instrument (Features/AddCustomInstrument) ends in a visible Verified/Failed
/// state — never stuck Unverified, never a 500 — once its <see cref="HistoryBackfillJob"/> run (T2.7)
/// completes. <see cref="HistoryBackfillJobTests"/> covers the job's original range/FX/idempotency
/// behavior for already-known-good instruments; this file covers only the verification-status
/// transition T2.8 added on top of it.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class HistoryBackfillVerificationTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task RunAsync_UnverifiedInstrument_SuccessfulFetch_MarksVerified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewUnverifiedInstrument("MSFT.US", PriceSource.Stooq, "USD");
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = new[] { new InstrumentQuote(instrument.Id, Today, 420m) };
        var source = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));
        var fxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.Success([new FxRateQuote("USDPLN", Today, 4m)]));

        var job = new HistoryBackfillJob(db, [source], fxSource, TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Verified, stored.VerificationStatus);
    }

    [Fact]
    public async Task RunAsync_UnverifiedInstrument_ErrorFetch_MarksFailedNotAnException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        // PLN-quoted so the job never needs to backfill FX alongside it (see RunAsync_PlnInstrument_
        // DoesNotBackfillFx in HistoryBackfillJobTests) — keeps this test scoped to the instrument's
        // own fetch outcome, without also having to script an FX source it doesn't care about.
        var instrument = NewUnverifiedInstrument("BOGUS.PL", PriceSource.Stooq, "PLN");
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(
            PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Error("malformed Stooq history payload: unrecognized header"));

        var job = new HistoryBackfillJob(db, [source], new ScriptedFxRateSource(), TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken); // must not throw — same "no 500" contract as the request path.

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Failed, stored.VerificationStatus);
    }

    [Fact]
    public async Task RunAsync_UnverifiedInstrument_NoDataFetch_MarksFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewUnverifiedInstrument("EMPTY.PL", PriceSource.Stooq, "PLN");
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.NoData());

        var job = new HistoryBackfillJob(db, [source], new ScriptedFxRateSource(), TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Failed, stored.VerificationStatus);
    }

    [Fact]
    public async Task RunAsync_AlreadyVerifiedInstrument_ErrorFetch_StaysVerified()
    {
        // Guards T2.9's future reuse of the same trigger for "instrument's first attach to an asset":
        // a catalog instrument that's already Verified must never flip to Failed just because one
        // backfill run had a hiccup — only a genuinely Unverified (custom, T2.8) instrument resolves.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "CDR.PL",
            Name = "CD Projekt",
            Source = PriceSource.Stooq,
            QuoteCurrency = "PLN", // PLN-quoted: skips FX backfill, see the note above.
            AssetClass = AssetClass.Stock,
            VerificationStatus = InstrumentVerificationStatus.Verified,
        };
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Error("transient failure"));

        var job = new HistoryBackfillJob(db, [source], new ScriptedFxRateSource(), TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, cancellationToken);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Verified, stored.VerificationStatus);
    }

    private static Instrument NewUnverifiedInstrument(string ticker, PriceSource source, string quoteCurrency) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = ticker,
        Name = ticker,
        Source = source,
        QuoteCurrency = quoteCurrency,
        AssetClass = AssetClass.Stock,
        VerificationStatus = InstrumentVerificationStatus.Unverified,
    };
}
