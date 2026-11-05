using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.MarketData.Tests;

public sealed class FxSyncJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
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
        await SeedExistingHistoryAsync(db, cancellationToken);

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
    public async Task FirstRun_FetchesLatestOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        // No FxRate row at all yet: the job stores only the latest rate; history is the backfill job's.

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln());

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        Assert.Equal(0, fxSource.HistoryFetchCount);
        foreach (var code in NonPlnCodes)
        {
            Assert.Equal(1, await db.FxRates.CountAsync(r => r.Pair == $"{code}PLN", cancellationToken));
        }
    }

    [Fact]
    public async Task UnexpectedException_FinishesRunFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await db.SeedFxRateAsync("EURPLN", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5), 4m, cancellationToken);

        // The startup gap fill is the only place the job reads RequestDelay, which is what throws.
        var job = new FxSyncJob(db, new ThrowingFxRateSource(), publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await Record.ExceptionAsync(() => job.RunAsync(cancellationToken, SyncRunSource.Startup));

        await using var readDb = CreateDbContext();
        var run = await readDb.SyncRuns.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.NotNull(run.FinishedAt);
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
        await SeedExistingHistoryAsync(db, cancellationToken);

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
        await SeedExistingHistoryAsync(db, cancellationToken);

        var fxSource = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.NoData());

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.True(run.NoDataCount > 0);
        Assert.NotEqual(SyncRunStatus.Failed, run.Status);
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

    [Fact]
    public async Task StartupRun_FillsGapSinceLastRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastRate = today.AddDays(-5);
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await SeedRatesAtAsync(db, lastRate, cancellationToken);

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln(), historyResultsByCode: GapHistory(today));

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken, SyncRunSource.Startup);

        Assert.Equal(NonPlnCodes.Order(), fxSource.HistoryFetches.Select(f => f.CurrencyCode).Order());
        Assert.All(fxSource.HistoryFetches, f => Assert.Equal(lastRate.AddDays(1), f.From));

        foreach (var code in NonPlnCodes)
        {
            var dates = await db.FxRates.Where(r => r.Pair == $"{code}PLN" && r.Date > lastRate && r.Date <= today)
                .Select(r => r.Date).OrderBy(d => d).ToListAsync(cancellationToken);
            Assert.Equal([today.AddDays(-3), today.AddDays(-2)], dates);
        }
    }

    [Fact]
    public async Task StartupRun_GapIsCappedAt30Days()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await SeedRatesAtAsync(db, today.AddDays(-60), cancellationToken);

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln(), historyResultsByCode: GapHistory(today));

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken, SyncRunSource.Startup);

        Assert.Equal(NonPlnCodes.Length, fxSource.HistoryFetchCount);
        Assert.All(fxSource.HistoryFetches, f => Assert.Equal(today.AddDays(-30), f.From));
    }

    [Fact]
    public async Task ScheduledRun_FetchesLatestOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await SeedRatesAtAsync(db, today.AddDays(-5), cancellationToken);

        var fxSource = new ScriptedFxRateSource(LatestForAllNonPln());

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        Assert.Equal(0, fxSource.HistoryFetchCount);
    }

    private static Dictionary<string, PriceFetchResult<FxRateQuote>> GapHistory(DateOnly today) => NonPlnCodes.ToDictionary(
        code => code,
        code => PriceFetchResult<FxRateQuote>.Success(
        [
            new FxRateQuote($"{code}PLN", today.AddDays(-3), 4.1m),
            new FxRateQuote($"{code}PLN", today.AddDays(-2), 4.2m),
        ]));

    private static async Task SeedRatesAtAsync(MarketDataDbContext db, DateOnly date, CancellationToken cancellationToken)
    {
        foreach (var code in NonPlnCodes)
        {
            await db.SeedFxRateAsync($"{code}PLN", date, 4m, cancellationToken);
        }
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
}
