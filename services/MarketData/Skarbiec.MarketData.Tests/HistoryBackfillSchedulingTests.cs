using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.AddCustomInstrument;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Http;

namespace Skarbiec.MarketData.Tests;

[Collection(TestingDefaults.SerialCollectionName)]
public sealed class HistoryBackfillSchedulingTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    [Fact]
    public async Task EnqueueAsync_ReturnsBeforeAnyFetch_ThenTheJobFiresOnItsOwnAndBackfillsFromTheRequestedDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid instrumentId;
        await using (var seedDb = CreateDbContext())
        {
            var instrument = new Instrument
            {
                Id = Guid.NewGuid(),
                Ticker = "AAPL.US",
                Name = "AAPL.US",
                Source = PriceSource.Yahoo,
                QuoteCurrency = "USD",
                AssetClass = AssetClass.Stock,
            };
            seedDb.Instruments.Add(instrument);
            await seedDb.SaveChangesAsync(cancellationToken);
            instrumentId = instrument.Id;
        }

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-365);
        var quotes = Enumerable.Range(0, 366).Select(offset => new InstrumentQuote(instrumentId, from.AddDays(offset), 100m)).ToList();
        var priceSource = new ScriptedPriceSource(PriceSource.Yahoo, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        // Completion signal: the job's span covers the whole run, so it stops only after the write commits.
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HistoryBackfillJob.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:marketdata-db"] = _containers.PostgresConnectionString;
        builder.Services.AddDbContext<MarketDataDbContext>(o => o.UseNpgsql(_containers.PostgresConnectionString));
        builder.Services.AddSingleton<IPriceSource>(priceSource);
        builder.Services.AddMassTransit(x => x.UsingInMemory());
        builder.Services.AddMarketDataScheduler(_containers.PostgresConnectionString);
        builder.AddHistoryBackfillJob();

        // Deliberately not disposed: Quartz's LogProvider caches this host's ILoggerFactory in a process-wide static.
        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            var trigger = host.Services.GetRequiredService<IHistoryBackfillTrigger>();

            await trigger.EnqueueAsync(instrumentId, from, cancellationToken);
            // Scheduling and firing happen on different threads, so right after EnqueueAsync nothing has been fetched.
            Assert.Equal(0, priceSource.HistoryFetchCount);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && activities.IsEmpty)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            Assert.NotEmpty(activities);
            Assert.Contains(activities, a => a.OperationName == "HistoryBackfillJob.Run");
            Assert.Equal(1, priceSource.HistoryFetchCount);

            await using var db = CreateDbContext();
            var storedQuotes = await db.PriceQuotes.Where(q => q.InstrumentId == instrumentId).ToListAsync(cancellationToken);
            Assert.Equal(quotes.Count, storedQuotes.Count);
            Assert.True(storedQuotes.Min(q => q.Date) <= from);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }

    // The slice under test is called directly; the fake trigger is the factory's.
    [Fact]
    public async Task AddCustomInstrument_DoesNotEnqueue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsJsonAsync(
            MarketDataApi.InstrumentsUri,
            new AddCustomInstrumentRequest
            {
                Ticker = "CDR.WA",
                Name = "CD Projekt",
                QuoteCurrency = "PLN",
                AssetClass = AssetClass.Stock,
            },
            cancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Empty(Factory.BackfillTrigger.Enqueued);
    }

    [Fact]
    public async Task WatchlistItemAdded_EnqueuesRecentWindow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = await SeedInstrumentAsync(historyCoveredFrom: null, cancellationToken);

        await PublishWatchlistItemAddedAsync(instrumentId, cancellationToken);

        await ConsumerTestHost.WaitForEnqueuedAsync(Factory.BackfillTrigger, instrumentId, 1, cancellationToken);
        Assert.Equal([DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14)], Factory.BackfillTrigger.FromsFor(instrumentId));
    }

    [Fact]
    public async Task WatchlistItemAdded_HistoryCoveredLaterThanWindow_EnqueuesRecentWindow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var instrumentId = await SeedInstrumentAsync(historyCoveredFrom: today.AddDays(-7), cancellationToken);

        await PublishWatchlistItemAddedAsync(instrumentId, cancellationToken);

        await ConsumerTestHost.WaitForEnqueuedAsync(Factory.BackfillTrigger, instrumentId, 1, cancellationToken);
        Assert.Equal([today.AddDays(-14)], Factory.BackfillTrigger.FromsFor(instrumentId));
    }

    [Fact]
    public async Task WatchlistItemAdded_CoveredInstrument_EnqueuesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var instrumentId = await SeedInstrumentAsync(historyCoveredFrom: today.AddDays(-14), cancellationToken);

        await PublishWatchlistItemAddedAsync(instrumentId, cancellationToken);

        // The usage row is written before the enqueue decision, so its appearance means the consumer has decided.
        await WaitForWatchCountAsync(instrumentId, cancellationToken);
        Assert.Empty(Factory.BackfillTrigger.FromsFor(instrumentId));
    }

    private async Task<Guid> SeedInstrumentAsync(DateOnly? historyCoveredFrom, CancellationToken cancellationToken)
    {
        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT.US",
            Name = "MSFT.US",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
            HistoryCoveredFrom = historyCoveredFrom,
        };

        await using var db = CreateDbContext();
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);
        return instrument.Id;
    }

    private async Task PublishWatchlistItemAddedAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        // The host's bus binds its queues on start, so readiness is awaited before publishing.
        using var client = Factory.CreateClient();
        using var ready = await client.GetReadyAsync(cancellationToken);
        Assert.True(ready.IsSuccessStatusCode);

        // The scoped publish endpoint is bus-outbox backed and would wait for a SaveChanges; the bus publishes straight away.
        var bus = Factory.Services.GetRequiredService<IBus>();
        await bus.Publish(WatchlistEvents.Added(Guid.NewGuid(), instrumentId), cancellationToken);
    }

    private async Task WaitForWatchCountAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db = CreateDbContext();
            if (await db.InstrumentUsages.AnyAsync(u => u.InstrumentId == instrumentId && u.WatchCount == 1, cancellationToken))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"No watch count recorded for instrument {instrumentId} within the deadline.");
    }
}
