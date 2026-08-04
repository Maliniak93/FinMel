using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Quartz enqueuing mechanics for backfill (T2.7 AC): triggering a backfill returns before the
/// external source is ever called, and the scheduled job then fires on its own — no manual
/// second trigger — landing the data. <see cref="HistoryBackfillJobTests"/> covers the job's own
/// backfill logic (range, FX, idempotency) without any of this scheduling machinery, mirroring how
/// <see cref="PriceSyncJobTests"/>/<see cref="PriceSyncSchedulingTests"/> split T2.6's tests.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class HistoryBackfillSchedulingTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // Explicit field: the primary constructor parameter is also passed to the base constructor
    // above, so referencing it directly elsewhere in this class would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string TablePrefix = "quartz.qrtz_"; // must match MarketDataDbContext's modelBuilder.AddQuartz schema/prefix.

    [Fact]
    public async Task EnqueueAsync_ReturnsBeforeAnyFetch_ThenTheJobFiresOnItsOwnAndBackfillsOneYear()
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
                Source = PriceSource.Stooq,
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
        var fxRates = Enumerable.Range(0, 366).Select(offset => new FxRateQuote("USDPLN", from.AddDays(offset), 4m)).ToList();
        var priceSource = new ScriptedPriceSource(PriceSource.Stooq, historyResult: PriceFetchResult<InstrumentQuote>.Success(quotes));

        // Completion signal for the polling loop below: HistoryFetchCount only proves the fetch was
        // *attempted*, not that the job's upsert/SaveChanges afterward has committed. The activity
        // this listener captures spans HistoryBackfillJob.RunAsync's entire body (T2.7, mirroring
        // PriceSyncJob's span), so ActivityStopped only fires once the DB write is done.
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
        builder.Services.AddSingleton<IFxRateSource>(new ScriptedFxRateSource(historyResult: PriceFetchResult<FxRateQuote>.Success(fxRates)));
        builder.Services.AddQuartz(q =>
        {
            q.SchedulerId = "AUTO"; // unique per instance — same reasoning as PriceSyncJobExtensions.

            q.UsePersistentStore(store =>
            {
                store.UsePostgres(c =>
                {
                    c.ConnectionString = _containers.PostgresConnectionString;
                    c.TablePrefix = TablePrefix;
                });
                store.UseSystemTextJsonSerializer();
                store.UseClustering();
            });
        });
        builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        builder.AddHistoryBackfillJob();

        // Deliberately not disposed — same reasoning as PriceSyncSchedulingTests: Quartz.Logging.LogProvider
        // caches this host's ILoggerFactory in a process-wide static once AddQuartzHostedService starts it.
        var host = builder.Build();
        await host.StartAsync(cancellationToken);
        try
        {
            var trigger = host.Services.GetRequiredService<IHistoryBackfillTrigger>();

            await trigger.EnqueueAsync(instrumentId, cancellationToken);
            // Scheduling a trigger and actually firing it happen on different threads — immediately
            // after EnqueueAsync returns, the job has not run yet (T2.7 AC: "no external call in the
            // request path").
            Assert.Equal(0, priceSource.HistoryFetchCount);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && activities.IsEmpty)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            Assert.NotEmpty(activities); // fired on its own, without a manual trigger.
            Assert.Contains(activities, a => a.OperationName == "HistoryBackfillJob.Run");
            Assert.Equal(1, priceSource.HistoryFetchCount);

            await using var db = CreateDbContext();
            var storedQuotes = await db.PriceQuotes.Where(q => q.InstrumentId == instrumentId).ToListAsync(cancellationToken);
            Assert.Equal(quotes.Count, storedQuotes.Count);
            Assert.True(storedQuotes.Min(q => q.Date) <= from);

            var storedFxRates = await db.FxRates.Where(r => r.Pair == "USDPLN").ToListAsync(cancellationToken);
            Assert.Equal(fxRates.Count, storedFxRates.Count);
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }
}
