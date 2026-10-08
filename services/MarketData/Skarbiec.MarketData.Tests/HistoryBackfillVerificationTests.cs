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

[Collection(TestingDefaults.CollectionName)]
public sealed class HistoryBackfillVerificationTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task RunAsync_UnverifiedInstrument_SuccessfulFetch_MarksVerified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewUnverifiedInstrument("MSFT.US", PriceSource.Yahoo, "USD");
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var quotes = new[] { new InstrumentQuote(instrument.Id, Today, 420m) };
        var source = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, Today.AddDays(-30), cancellationToken);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Verified, stored.VerificationStatus);
    }

    [Fact]
    public async Task RunAsync_UnverifiedInstrument_ErrorFetch_MarksFailedNotAnException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewUnverifiedInstrument("BOGUS.PL", PriceSource.Yahoo, "PLN");
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(
            PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Error("malformed Yahoo history payload: unrecognized header"));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, Today.AddDays(-30), cancellationToken);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Failed, stored.VerificationStatus);
    }

    [Fact]
    public async Task RunAsync_UnverifiedInstrument_NoDataFetch_MarksFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = NewUnverifiedInstrument("EMPTY.PL", PriceSource.Yahoo, "PLN");
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.NoData());

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, Today.AddDays(-30), cancellationToken);

        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == instrument.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Failed, stored.VerificationStatus);
    }

    [Fact]
    public async Task RunAsync_AlreadyVerifiedInstrument_ErrorFetch_StaysVerified()
    {
        // A Verified catalog instrument must never flip to Failed over one bad backfill run.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();

        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "CDR.PL",
            Name = "CD Projekt",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Stock,
            VerificationStatus = InstrumentVerificationStatus.Verified,
        };
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Error("transient failure"));

        var job = new HistoryBackfillJob(db, [source], TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, Today.AddDays(-30), cancellationToken);

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
