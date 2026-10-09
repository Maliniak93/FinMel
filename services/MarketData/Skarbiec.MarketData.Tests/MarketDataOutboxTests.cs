using System.Text.Json;
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
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.MarketData.Tests;

// Builds its own provider so no hosted service starts and the outbox row is never delivered before the assertion reads it.
public sealed class MarketDataOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
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

        // PLN needs no FX, so the run's outcome depends only on the one source below.
        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple",
            Source = PriceSource.Yahoo,
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

        var source = new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, Today, 100m)]));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Completed, run.Status);

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.Contains(outboxMessages, m => m.MessageType.Contains(nameof(DailyPricesSynced)));
    }

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
            Source = PriceSource.Yahoo,
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

        // The only source errors out: nothing synced or empty, so the run is Failed, not Partial.
        var source = new ScriptedPriceSource(PriceSource.Yahoo, PriceFetchResult<InstrumentQuote>.Error("down"));

        var job = new PriceSyncJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunStatus.Failed, run.Status);

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.DoesNotContain(outboxMessages, m => m.MessageType.Contains(nameof(DailyPricesSynced)));
    }

    [Fact]
    public async Task HistoryBackfill_Success_WritesInstrumentHistoryBackfilledOutboxMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        // Covered from 2026-01-01, so the window's end is 2025-12-31 whatever today is.
        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Stock,
            HistoryCoveredFrom = new DateOnly(2026, 1, 1),
        };
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(
        [
            new InstrumentQuote(instrument.Id, new DateOnly(2025, 3, 1), 100m),
            new InstrumentQuote(instrument.Id, new DateOnly(2025, 12, 31), 110m),
        ]));

        var job = new HistoryBackfillJob(db, [source], publishEndpoint, TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        await job.RunAsync(instrument.Id, new DateOnly(2025, 3, 1), cancellationToken);

        var run = await db.SyncRuns.SingleAsync(cancellationToken);
        Assert.Equal(SyncRunKind.Backfill, run.Kind);
        Assert.Equal(2, await db.PriceQuotes.CountAsync(cancellationToken));

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        var outboxMessage = Assert.Single(outboxMessages, m => m.MessageType.Contains(nameof(InstrumentHistoryBackfilled)));

        using var envelope = JsonDocument.Parse(outboxMessage.Body);
        var payload = envelope.RootElement.GetProperty("message");
        Assert.Equal(instrument.Id, payload.GetProperty("instrumentId").GetGuid());
        Assert.Equal("2025-03-01", payload.GetProperty("from").GetString());
        Assert.Equal("2025-12-31", payload.GetProperty("to").GetString());
    }

    [Fact]
    public async Task HistoryBackfill_NoDataOrError_DoesNotPublish()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var noDataInstrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Stock,
            HistoryCoveredFrom = new DateOnly(2026, 1, 1),
        };
        var errorInstrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "XAU",
            Name = "Gold",
            Source = PriceSource.GoldApi,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.PreciousMetal,
            HistoryCoveredFrom = new DateOnly(2026, 1, 1),
        };
        db.Instruments.AddRange(noDataInstrument, errorInstrument);
        await db.SaveChangesAsync(cancellationToken);

        IPriceSource[] sources =
        [
            new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.NoData()),
            new ScriptedPriceSource(PriceSource.GoldApi, historyResult: PriceFetchResult<InstrumentQuote>.Error("down")),
        ];

        var job = new HistoryBackfillJob(db, sources, publishEndpoint, TimeProvider.System, NullLogger<HistoryBackfillJob>.Instance);
        var from = new DateOnly(2025, 3, 1);
        await job.RunAsync(noDataInstrument.Id, from, cancellationToken);
        await job.RunAsync(errorInstrument.Id, from, cancellationToken);

        var run = await db.SyncRuns.Where(r => r.Kind == SyncRunKind.Backfill).ToListAsync(cancellationToken);
        Assert.Equal(2, run.Count);

        var outboxMessages = await db.Set<OutboxMessage>().ToListAsync(cancellationToken);
        Assert.DoesNotContain(outboxMessages, m => m.MessageType.Contains(nameof(InstrumentHistoryBackfilled)));
    }
}
