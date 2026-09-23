using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Proves the outbox row and the <c>SyncRun</c> completion write commit atomically (T2.10 AC, mirrors
/// Identity's <c>UserRegisteredOutboxTests</c>/Portfolio's <c>PortfolioOutboxTests</c>, T0.10/T1.5).
/// Deliberately builds its own <see cref="ServiceProvider"/> via <see cref="HostlessOutboxProvider"/>
/// instead of the WebApplicationFactory-backed <c>MarketDataApiFactory</c>: no <see cref="IHostedService"/>
/// (the bus, the outbox delivery poller) is ever started, so the row can never be delivered/removed
/// before the assertion runs.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class MarketDataOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 8, 6);

    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = HostlessOutboxProvider.Build<MarketDataDbContext>(containers, _ => { });

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MarketDataDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task RunAsync_SuccessfulRun_WritesDailyPricesSyncedOutboxMessageInSameTransactionAsSyncRunRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        // PLN (ADR-008 base currency) needs no FX conversion, and spec-04 design decision 6 drops
        // PriceSyncJob's FX dependency entirely, so the run's outcome depends only on the one price
        // source below — keeps this test's status/count math unambiguous.
        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple",
            Source = PriceSource.Stooq,
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Stock,
        };
        db.Instruments.Add(instrument);
        db.InstrumentUsages.Add(new InstrumentUsage
        {
            InstrumentId = instrument.Id,
            AssetCount = 1,
            FirstUsedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 100m)]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Completed, run.Status);

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.Contains(outboxMessages, m => m.MessageType.Contains(nameof(DailyPricesSynced)));
    }

    /// <summary>spec-04 AC9: FxSyncJob's completion write and its Fx-kind DailyPricesSynced outbox
    /// row commit in the same transaction — same pattern as PriceSyncJob's own outbox test above, on
    /// the hostless provider so the delivery poller can never race the assertion.</summary>
    [Fact]
    public async Task RunAsync_FxRun_WritesDailyPricesSyncedFxOutboxMessageInSameTransactionAsSyncRunRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await db.SeedCurrencyCatalogAsync(cancellationToken);
        foreach (var code in new[] { "EUR", "USD", "GBP", "CHF" })
        {
            await db.SeedFxRateAsync($"{code}PLN", Today.AddDays(-30), 4m, cancellationToken);
        }

        var fxSource = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success(
        [
            new FxRateQuote("EURPLN", Today, 4.30m),
            new FxRateQuote("USDPLN", Today, 3.65m),
            new FxRateQuote("GBPPLN", Today, 4.90m),
            new FxRateQuote("CHFPLN", Today, 4.55m),
        ]));

        var job = new FxSyncJob(db, fxSource, publishEndpoint, TimeProvider.System, NullLogger<FxSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunKind.Fx, run.Kind);
        Assert.True(run.Status is SyncRunStatus.Completed or SyncRunStatus.Partial);

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.Contains(outboxMessages, m => m.MessageType.Contains(nameof(DailyPricesSynced)));
    }

    [Fact]
    public async Task RunAsync_WhollyFailedRun_DoesNotPublish()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple",
            Source = PriceSource.Stooq,
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Stock,
        };
        db.Instruments.Add(instrument);
        db.InstrumentUsages.Add(new InstrumentUsage
        {
            InstrumentId = instrument.Id,
            AssetCount = 1,
            FirstUsedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        // The only source errors out entirely -> synced=0, noData=0, failed=1 -> Failed (not Partial).
        var source = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Error("down"));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Failed, run.Status);

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.DoesNotContain(outboxMessages, m => m.MessageType.Contains(nameof(DailyPricesSynced)));
    }
}
