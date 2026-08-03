using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Daily sync: FX rates + quotes for every instrument in the dictionary, isolating per-source
/// failures so one bad vendor doesn't stop the rest (E4 [M], ADR-007). External APIs are called only
/// from here — <see cref="IPriceSource"/>/<see cref="IFxRateSource"/> implementations never run in a
/// request path (enforced by <c>ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions</c>).
/// </summary>
/// <remarks>
/// "In use" (E4 AC: "only instruments attached to assets") can't be determined yet — Portfolio has no
/// <c>InstrumentId</c> on <c>Asset</c> until T2.9 lands. Until then, every instrument in MarketData's
/// own dictionary stands in for "in use"; swap <see cref="GetInstrumentsToSyncAsync"/> for a Portfolio
/// REST call (or a locally-maintained usage set from <c>AssetChanged</c>) once T2.9 exists.
/// </remarks>
[DisallowConcurrentExecution]
public sealed class PriceSyncJob(
    MarketDataDbContext db,
    IEnumerable<IPriceSource> priceSources,
    IFxRateSource fxRateSource,
    TimeProvider timeProvider,
    ILogger<PriceSyncJob> logger) : IJob
{
    public static readonly JobKey Key = new("price-sync", "market-data");

    /// <summary>Registered with OpenTelemetry tracing in Program.cs — ServiceDefaults only adds the
    /// app's own ApplicationName-named source plus MassTransit's, not this one.</summary>
    public const string ActivitySourceName = "Skarbiec.MarketData.PriceSyncJob";

    private const string BaseCurrency = "PLN"; // ADR-008

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Quartz-independent entry point — lets tests drive a run directly instead of faking
    /// <see cref="IJobExecutionContext"/>.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("PriceSyncJob.Run");

        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var instruments = await GetInstrumentsToSyncAsync(cancellationToken);
        var sourcesByType = priceSources.ToDictionary(s => s.Source);

        var synced = 0;
        var noData = 0;
        var failed = 0;

        foreach (var group in instruments.GroupBy(i => i.Source))
        {
            var groupInstruments = group.ToList();

            if (!sourcesByType.TryGetValue(group.Key, out var source))
            {
                logger.LogWarning(
                    "PriceSyncJob: no IPriceSource registered for {Source}; treating {Count} instrument(s) as failed.",
                    group.Key, groupInstruments.Count);
                failed += groupInstruments.Count;
                continue;
            }

            var result = await FetchSafelyAsync(
                () => source.FetchLatestAsync(groupInstruments, cancellationToken),
                ex => $"{group.Key} fetch threw unexpectedly: {ex.Message}");

            switch (result.Outcome)
            {
                case PriceFetchOutcome.Success:
                    var returnedIds = await UpsertInstrumentQuotesAsync(result.Values, cancellationToken);
                    synced += returnedIds.Count;
                    failed += groupInstruments.Count(i => !returnedIds.Contains(i.Id));
                    break;
                case PriceFetchOutcome.NoData:
                    noData += groupInstruments.Count;
                    break;
                case PriceFetchOutcome.Error:
                    logger.LogWarning("PriceSyncJob: {Source} fetch failed: {Reason}", group.Key, result.ErrorReason);
                    failed += groupInstruments.Count;
                    break;
            }

            if (source.RequestDelay > TimeSpan.Zero)
            {
                await Task.Delay(source.RequestDelay, cancellationToken);
            }
        }

        var currencies = instruments
            .Select(i => i.QuoteCurrency)
            .Where(c => !string.Equals(c, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currencies.Count > 0)
        {
            var fxResult = await FetchSafelyAsync(
                () => fxRateSource.FetchLatestAsync(currencies, cancellationToken),
                ex => $"FX fetch threw unexpectedly: {ex.Message}");

            switch (fxResult.Outcome)
            {
                case PriceFetchOutcome.Success:
                    var syncedPairs = await UpsertFxRatesAsync(fxResult.Values, cancellationToken);
                    var syncedCurrencies = currencies.Count(c => syncedPairs.Contains(c.ToUpperInvariant() + BaseCurrency));
                    synced += syncedCurrencies;
                    failed += currencies.Count - syncedCurrencies;
                    break;
                case PriceFetchOutcome.NoData:
                    noData += currencies.Count;
                    break;
                case PriceFetchOutcome.Error:
                    logger.LogWarning("PriceSyncJob: FX fetch failed: {Reason}", fxResult.ErrorReason);
                    failed += currencies.Count;
                    break;
            }
        }

        run.FinishedAt = timeProvider.GetUtcNow();
        run.SyncedCount = synced;
        run.NoDataCount = noData;
        run.FailedCount = failed;
        run.Status = DetermineStatus(synced, noData, failed);
        await db.SaveChangesAsync(cancellationToken);

        activity?.SetTag("skarbiec.sync_run.id", run.Id);
        activity?.SetTag("skarbiec.sync_run.status", run.Status.ToString());
        activity?.SetTag("skarbiec.sync_run.synced", synced);
        activity?.SetTag("skarbiec.sync_run.no_data", noData);
        activity?.SetTag("skarbiec.sync_run.failed", failed);

        logger.LogInformation(
            "PriceSyncJob run {RunId} finished: {Status} (synced={Synced}, noData={NoData}, failed={Failed}).",
            run.Id, run.Status, synced, noData, failed);
    }

    private static SyncRunStatus DetermineStatus(int synced, int noData, int failed) => failed switch
    {
        0 => SyncRunStatus.Completed,
        _ when synced > 0 || noData > 0 => SyncRunStatus.Partial,
        _ => SyncRunStatus.Failed,
    };

    private Task<List<Instrument>> GetInstrumentsToSyncAsync(CancellationToken cancellationToken) =>
        db.Instruments.AsNoTracking().ToListAsync(cancellationToken);

    private static async Task<PriceFetchResult<TValue>> FetchSafelyAsync<TValue>(
        Func<Task<PriceFetchResult<TValue>>> fetch, Func<Exception, string> describeError)
    {
        try
        {
            return await fetch();
        }
        catch (Exception ex)
        {
            return PriceFetchResult<TValue>.Error(describeError(ex));
        }
    }

    private async Task<HashSet<Guid>> UpsertInstrumentQuotesAsync(
        IReadOnlyList<InstrumentQuote> quotes, CancellationToken cancellationToken)
    {
        if (quotes.Count == 0)
        {
            return [];
        }

        var instrumentIds = quotes.Select(q => q.InstrumentId).ToHashSet();
        var dates = quotes.Select(q => q.Date).ToHashSet();
        var existing = await db.PriceQuotes
            .Where(q => instrumentIds.Contains(q.InstrumentId) && dates.Contains(q.Date))
            .ToDictionaryAsync(q => (q.InstrumentId, q.Date), cancellationToken);

        foreach (var quote in quotes)
        {
            if (existing.TryGetValue((quote.InstrumentId, quote.Date), out var row))
            {
                row.Close = quote.Close;
            }
            else
            {
                db.PriceQuotes.Add(new PriceQuote
                {
                    Id = Guid.NewGuid(),
                    InstrumentId = quote.InstrumentId,
                    Date = quote.Date,
                    Close = quote.Close,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return instrumentIds;
    }

    private async Task<HashSet<string>> UpsertFxRatesAsync(
        IReadOnlyList<FxRateQuote> rates, CancellationToken cancellationToken)
    {
        if (rates.Count == 0)
        {
            return [];
        }

        var pairs = rates.Select(r => r.Pair).ToHashSet();
        var dates = rates.Select(r => r.Date).ToHashSet();
        var existing = await db.FxRates
            .Where(r => pairs.Contains(r.Pair) && dates.Contains(r.Date))
            .ToDictionaryAsync(r => (r.Pair, r.Date), cancellationToken);

        foreach (var rate in rates)
        {
            if (existing.TryGetValue((rate.Pair, rate.Date), out var row))
            {
                row.Rate = rate.Rate;
            }
            else
            {
                db.FxRates.Add(new FxRate
                {
                    Id = Guid.NewGuid(),
                    Pair = rate.Pair,
                    Date = rate.Date,
                    Rate = rate.Rate,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return pairs;
    }
}
