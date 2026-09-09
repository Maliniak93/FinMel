using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
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

        // PLN (ADR-008 base currency) needs no FX conversion, so the run's outcome depends only on
        // the one price source below — keeps this test's status/count math unambiguous.
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
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 100m)]));
        // M1.4: the FX branch is no longer conditional on a non-PLN instrument existing — it always
        // runs (SupportedCurrencies.All unions in EUR/USD). This instrument is PLN-quoted, so neither
        // requested currency is instrument-backed here; an empty Success response is enough to keep
        // FailedCount at 0 (the M1.4 FailedCount decision — see PriceSyncJob.RunAsync).
        var fx = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success([]));

        var job = new PriceSyncJob(db, [source], fx, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Completed, run.Status);

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
        await db.SaveChangesAsync(cancellationToken);

        // The only source errors out entirely -> synced=0, noData=0, failed=1 -> Failed (not Partial).
        // M1.4: EUR/USD are still requested (SupportedCurrencies.All) even though this instrument is
        // PLN-quoted, but neither is instrument-backed here, so an empty Success response contributes
        // nothing to synced/failed/noData — the run's outcome still depends only on the price source.
        var source = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Error("down"));
        var fx = new ScriptedFxRateSource(PriceFetchResult<FxRateQuote>.Success([]));

        var job = new PriceSyncJob(db, [source], fx, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Failed, run.Status);

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.DoesNotContain(outboxMessages, m => m.MessageType.Contains(nameof(DailyPricesSynced)));
    }
}
