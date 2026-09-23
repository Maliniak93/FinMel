using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// FxSyncJob's business logic (spec-04 AC3-8): a latest-rate sync for every catalog currency except
/// PLN, a first-use 12-month backfill (never repeated once history exists), per-currency backfill
/// failure isolation, and the Fx-kind <see cref="SyncRun"/> write — exercised via
/// <see cref="FxSyncJob.RunAsync"/> directly, no Quartz scheduler involved, mirroring
/// <see cref="PriceSyncJobTests"/>. Scheduling mechanics are covered separately in
/// <see cref="FxSyncSchedulingTests"/>; outbox atomicity (AC9) in <see cref="MarketDataOutboxTests"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class FxSyncJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // Explicit field: the primary constructor parameter is also passed to the base constructor
    // above, so referencing it directly elsewhere in this class would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private static readonly DateOnly Today = new(2026, 9, 22);

    private static readonly string[] NonPlnCodes = ["EUR", "USD", "GBP", "CHF"];

    [Fact]
    public async Task RunAsync_UpsertsLatestRateForEveryCatalogCurrencyExceptPln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await SeedExistingHistoryAsync(db, cancellationToken); // no currency is a first-use here.

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln());

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        foreach (var code in NonPlnCodes)
        {
            Assert.True(
                await db.FxRates.AnyAsync(r => r.Pair == $"{code}PLN" && r.Date == Today, cancellationToken),
                $"expected a {code}PLN rate for {Today}.");
        }

        Assert.False(await db.FxRates.AnyAsync(r => r.Pair == "PLNPLN", cancellationToken));
    }

    [Fact]
    public async Task RunAsync_FirstRunForCurrency_BackfillsTwelveMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        // No FxRate row at all yet — every non-PLN currency needs a first-use backfill.

        var history = OneYearOfDates().Select(d => new FxRateQuote("EURPLN", d, 4m)).ToList();
        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln(), PriceFetchResult<FxRateQuote>.Success(history));

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        // Read the range from the arguments the job passed, not from the canned history (which the
        // fake returns regardless of from/to): exactly one call per non-PLN code, each ≥ 365 days.
        Assert.Equal(NonPlnCodes.Order(), fxSource.HistoryFetches.Select(f => f.CurrencyCode).Order());
        Assert.All(fxSource.HistoryFetches, f => Assert.True(
            f.To.DayNumber - f.From.DayNumber >= 365,
            $"{f.CurrencyCode} backfill requested {f.From}..{f.To}, shorter than 365 days."));

        var earliestEur = await db.FxRates.Where(r => r.Pair == "EURPLN").MinAsync(r => r.Date, cancellationToken);
        Assert.True(Today.DayNumber - earliestEur.DayNumber >= 365);
    }

    [Fact]
    public async Task RunAsync_SecondRun_DoesNotBackfillAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await SeedExistingHistoryAsync(db, cancellationToken); // simulates a prior run's backfill.

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln());

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        Assert.Equal(0, fxSource.HistoryFetchCount);
    }

    [Fact]
    public async Task RunAsync_NoDataDay_RecordsNoDataWithoutFailing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await SeedExistingHistoryAsync(db, cancellationToken); // no backfill involved — isolates the NoData path.

        var fxSource = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.NoData());

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.True(run.NoDataCount > 0);
        Assert.NotEqual(SyncRunStatus.Failed, run.Status);
    }

    [Fact]
    public async Task RunAsync_OneCurrencyBackfillFails_OthersStillSynced_RunRecordedAsPartial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        // No history for any currency — every one is a first-use backfill; EUR's is scripted to fail,
        // every other currency's succeeds (ScriptedFxRateSource's per-code override).
        var historyResultsByCode = NonPlnCodes.ToDictionary(
            code => code,
            code => code == "EUR"
                ? PriceFetchResult<FxRateQuote>.Error("simulated failure")
                : PriceFetchResult<FxRateQuote>.Success(OneYearOfDates().Select(d => new FxRateQuote($"{code}PLN", d, 4m)).ToList()));

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln(), historyResultsByCode: historyResultsByCode);

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Partial, run.Status);
        Assert.True(run.FailedCount >= 1);

        Assert.True(await db.FxRates.AnyAsync(r => r.Pair == "USDPLN" && r.Date == Today, cancellationToken));
        Assert.True(await db.FxRates.AnyAsync(r => r.Pair == "GBPPLN" && r.Date == Today, cancellationToken));
        Assert.True(await db.FxRates.AnyAsync(r => r.Pair == "CHFPLN" && r.Date == Today, cancellationToken));
    }

    [Fact]
    public async Task RunAsync_BackfillFailed_WritesNoLatestRateForThatCurrency_AndRetriesBackfillNextRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);

        // Run 1: EUR's first-use backfill fails, every other currency's succeeds. The latest-rate
        // result still carries EURPLN, so only the job itself can keep it out of the table.
        var firstRunSource = new ScriptedFxRateSource(
            LatestForAllNonPln(),
            historyResultsByCode: NonPlnCodes.ToDictionary(
                code => code,
                code => code == "EUR"
                    ? PriceFetchResult<FxRateQuote>.Error("simulated failure")
                    : PriceFetchResult<FxRateQuote>.Success(OneYearOfDates().Select(d => new FxRateQuote($"{code}PLN", d, 4m)).ToList())));

        await new FxSyncJob(db, firstRunSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance)
            .RunAsync(cancellationToken);

        Assert.False(
            await db.FxRates.AnyAsync(r => r.Pair == "EURPLN", cancellationToken),
            "a currency whose backfill failed must keep zero FxRate rows so the next run retries it.");

        // Run 2: every backfill succeeds — EUR, and only EUR, is backfilled again.
        var secondRunSource = new ScriptedFxRateSource(
            LatestForAllNonPln(),
            PriceFetchResult<FxRateQuote>.Success(OneYearOfDates().Select(d => new FxRateQuote("EURPLN", d, 4m)).ToList()));

        await new FxSyncJob(db, secondRunSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance)
            .RunAsync(cancellationToken);

        Assert.Equal("EUR", Assert.Single(secondRunSource.HistoryFetches).CurrencyCode);
        Assert.True(await db.FxRates.AnyAsync(r => r.Pair == "EURPLN" && r.Date == Today, cancellationToken));
    }

    [Fact]
    public async Task RunAsync_WritesSyncRunWithKindFx()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await SeedExistingHistoryAsync(db, cancellationToken);

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln());

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunKind.Fx, run.Kind);
    }

    private static PriceFetchResult<FxRateQuote> LatestForAllNonPln() => PriceFetchResult<FxRateQuote>.Success(
    [
        new FxRateQuote("EURPLN", Today, 4.30m),
        new FxRateQuote("USDPLN", Today, 3.65m),
        new FxRateQuote("GBPPLN", Today, 4.90m),
        new FxRateQuote("CHFPLN", Today, 4.55m),
    ]);

    private static async Task SeedExistingHistoryAsync(MarketDataDbContext db, CancellationToken cancellationToken)
    {
        foreach (var code in NonPlnCodes)
        {
            await db.SeedFxRateAsync($"{code}PLN", Today.AddDays(-30), 4m, cancellationToken);
        }
    }

    private static List<DateOnly> OneYearOfDates()
    {
        var start = Today.AddDays(-365);
        return Enumerable.Range(0, 366).Select(offset => start.AddDays(offset)).ToList();
    }
}
