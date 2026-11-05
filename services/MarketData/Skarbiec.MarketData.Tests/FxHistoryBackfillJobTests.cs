using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
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

public sealed class FxHistoryBackfillJobTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it in a fact would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly string[] NonPlnCodes = ["EUR", "USD", "GBP", "CHF"];

    [Fact]
    public async Task ExtendsBackToOldestTransactionMinusMargin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await db.SeedAssetLinkAsync(new DateOnly(2023, 11, 30), cancellationToken);
        await db.SeedAssetLinkAsync(new DateOnly(2024, 6, 1), cancellationToken);
        await db.SeedFxRateAsync("USDPLN", new DateOnly(2025, 10, 23), 3.7m, cancellationToken);

        var needDate = new DateOnly(2023, 11, 16);
        var fxSource = new ScriptedFxRateSource(
            historyResult: PriceFetchResult<FxRateQuote>.NoData(),
            historyResultsByCode: new Dictionary<string, PriceFetchResult<FxRateQuote>>
            {
                ["USD"] = PriceFetchResult<FxRateQuote>.Success(
                [
                    new FxRateQuote("USDPLN", needDate, 4.1m),
                    new FxRateQuote("USDPLN", needDate.AddDays(1), 4.2m),
                    new FxRateQuote("USDPLN", new DateOnly(2025, 10, 22), 3.6m),
                ]),
            });

        await NewJob(db, fxSource, scope).RunAsync(cancellationToken);

        var usdFetch = Assert.Single(fxSource.HistoryFetches, f => f.CurrencyCode == "USD");
        Assert.Equal(needDate, usdFetch.From);
        Assert.Equal(new DateOnly(2025, 10, 22), usdFetch.To);

        await using var readDb = CreateDbContext();
        var usdDates = await readDb.FxRates.Where(r => r.Pair == "USDPLN").Select(r => r.Date).OrderBy(d => d).ToListAsync(cancellationToken);
        Assert.Equal([needDate, needDate.AddDays(1), new DateOnly(2025, 10, 22), new DateOnly(2025, 10, 23)], usdDates);

        var usd = await readDb.Currencies.AsNoTracking().SingleAsync(c => c.Code == "USD", cancellationToken);
        Assert.Equal(needDate, usd.HistoryCoveredFrom);
    }

    [Fact]
    public async Task WithoutTransactions_UsesMinimumHorizon()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);

        var fxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.NoData());

        await NewJob(db, fxSource, scope).RunAsync(cancellationToken);

        var needDate = Today.AddDays(-365 - 14);
        Assert.Equal(NonPlnCodes.Order(), fxSource.HistoryFetches.Select(f => f.CurrencyCode).Order());
        Assert.All(fxSource.HistoryFetches, f => Assert.Equal(needDate, f.From));

        await using var readDb = CreateDbContext();
        var coverage = await readDb.Currencies.AsNoTracking().Where(c => c.Code != "PLN").Select(c => c.HistoryCoveredFrom).ToListAsync(cancellationToken);
        Assert.All(coverage, c => Assert.Equal(needDate, c));
    }

    [Fact]
    public async Task CoveredCurrency_MakesNoCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        var needDate = Today.AddDays(-365 - 14);
        await db.SeedCurrencyCatalogAsync(cancellationToken, historyCoveredFrom: needDate);
        await db.SeedFxRateAsync("USDPLN", Today.AddDays(-30), 4m, cancellationToken);

        var fxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.NoData());

        await NewJob(db, fxSource, scope).RunAsync(cancellationToken);

        Assert.Equal(0, fxSource.HistoryFetchCount);

        await using var readDb = CreateDbContext();
        Assert.Equal(1, await readDb.FxRates.CountAsync(cancellationToken));
        var coverage = await readDb.Currencies.AsNoTracking().Where(c => c.Code != "PLN").Select(c => c.HistoryCoveredFrom).ToListAsync(cancellationToken);
        Assert.All(coverage, c => Assert.Equal(needDate, c));
    }

    [Fact]
    public async Task Backfill_NeverShortensCoverage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        var earlier = Today.AddDays(-1000);
        await db.SeedCurrencyCatalogAsync(cancellationToken);
        await db.Currencies.Where(c => c.Code == "USD")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.HistoryCoveredFrom, (DateOnly?)earlier), cancellationToken);

        var fxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.NoData());

        await NewJob(db, fxSource, scope).RunAsync(cancellationToken);

        Assert.DoesNotContain(fxSource.HistoryFetches, f => f.CurrencyCode == "USD");

        await using var readDb = CreateDbContext();
        var usd = await readDb.Currencies.AsNoTracking().SingleAsync(c => c.Code == "USD", cancellationToken);
        Assert.Equal(earlier, usd.HistoryCoveredFrom);
    }

    [Fact]
    public async Task Error_KeepsCoverageOfThatCurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);

        var needDate = Today.AddDays(-365 - 14);
        var fxSource = new ScriptedFxRateSource(
            historyResult: PriceFetchResult<FxRateQuote>.NoData(),
            historyResultsByCode: new Dictionary<string, PriceFetchResult<FxRateQuote>>
            {
                ["EUR"] = PriceFetchResult<FxRateQuote>.Error("simulated failure"),
                ["USD"] = PriceFetchResult<FxRateQuote>.Success([new FxRateQuote("USDPLN", needDate, 4m)]),
            });

        await NewJob(db, fxSource, scope).RunAsync(cancellationToken);

        await using var readDb = CreateDbContext();
        var coverage = await readDb.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.HistoryCoveredFrom, cancellationToken);
        Assert.Null(coverage["EUR"]);
        Assert.Equal(needDate, coverage["USD"]);
        Assert.Equal(needDate, coverage["GBP"]);

        var outboxMessages = await readDb.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.Single(outboxMessages, m => m.MessageType.Contains("FxHistoryBackfilled"));
    }

    [Fact]
    public async Task NoData_MarksCovered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(_containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);

        var fxSource = new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.NoData());

        await NewJob(db, fxSource, scope).RunAsync(cancellationToken);

        await using var readDb = CreateDbContext();
        var coverage = await readDb.Currencies.AsNoTracking().Where(c => c.Code != "PLN").Select(c => c.HistoryCoveredFrom).ToListAsync(cancellationToken);
        Assert.Equal(NonPlnCodes.Length, coverage.Count);
        Assert.All(coverage, c => Assert.Equal(Today.AddDays(-365 - 14), c));
        Assert.Equal(0, await readDb.FxRates.CountAsync(cancellationToken));
    }

    private static FxHistoryBackfillJob NewJob(MarketDataDbContext db, IFxRateSource source, AsyncServiceScope scope) =>
        new(db, source, scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), TimeProvider.System, NullLogger<FxHistoryBackfillJob>.Instance);
}
