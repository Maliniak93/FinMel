using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

// "In use" comes from the InstrumentUsage read model built from Portfolio's events, never a call back to Portfolio.
[DisallowConcurrentExecution]
public sealed class PriceSyncJob(
    MarketDataDbContext db,
    IEnumerable<IPriceSource> priceSources,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider,
    ILogger<PriceSyncJob> logger) : IJob
{
    public static readonly JobKey Key = new("price-sync", "market-data");

    // Registered in Program.cs: ServiceDefaults adds only the app's own and MassTransit's sources.
    public const string ActivitySourceName = "Skarbiec.MarketData.PriceSyncJob";

    internal const int MaxGapDays = 30;

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await RunAsync(cancellationToken, SyncJobData.ReadSource(context));

    // Quartz-independent entry point, so tests drive a run without faking IJobExecutionContext.
    public async Task RunAsync(CancellationToken cancellationToken, SyncRunSource source = SyncRunSource.Schedule)
    {
        using var activity = ActivitySource.StartActivity("PriceSyncJob.Run");

        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKind.Prices,
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
            Source = source,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await SyncAsync(run, activity, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            db.SyncRuns.Attach(run);
            run.Finish(timeProvider.GetUtcNow(), synced: 0, noData: 0, failed: 1);
            await db.SaveChangesAsync(cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "PriceSyncJob run {RunId} failed: {Reason}", run.Id, ex.Message);
        }
    }

    private async Task SyncAsync(SyncRun run, Activity? activity, CancellationToken cancellationToken)
    {
        var (instruments, skipped) = await GetInstrumentsToSyncAsync(cancellationToken);
        var sourcesByType = priceSources.ToDictionary(s => s.Source);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var lastQuoteDates = run.Source == SyncRunSource.Startup
            ? await GetLastQuoteDatesAsync(instruments, cancellationToken)
            : [];

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

            var latestInstruments = groupInstruments;

            if (run.Source == SyncRunSource.Startup)
            {
                var gapInstruments = new List<(Instrument Instrument, DateOnly From)>();
                latestInstruments = [];
                foreach (var instrument in groupInstruments)
                {
                    if (lastQuoteDates.TryGetValue(instrument.Id, out var lastDate) && ClampGapStart(lastDate, today) is { } from)
                    {
                        gapInstruments.Add((instrument, from));
                    }
                    else
                    {
                        latestInstruments.Add(instrument);
                    }
                }

                foreach (var (instrument, from) in gapInstruments)
                {
                    var gapResult = await SafeFetch.RunAsync(
                        () => source.FetchHistoryAsync(instrument, from, today, cancellationToken),
                        ex => $"{group.Key} history fetch for {instrument.Ticker} threw unexpectedly: {ex.Message}");

                    switch (gapResult.Outcome)
                    {
                        case PriceFetchOutcome.Success when gapResult.Values.Count > 0:
                            await QuoteUpsert.UpsertInstrumentQuotesAsync(db, gapResult.Values, cancellationToken);
                            synced++;
                            break;
                        case PriceFetchOutcome.Success:
                        case PriceFetchOutcome.NoData:
                            noData++;
                            break;
                        case PriceFetchOutcome.Error:
                            logger.LogWarning(
                                "PriceSyncJob: {Source} history fetch for instrument {InstrumentId} failed: {Reason}",
                                group.Key, instrument.Id, gapResult.ErrorReason);
                            failed++;
                            break;
                    }

                    if (source.RequestDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(source.RequestDelay, cancellationToken);
                    }
                }
            }

            if (latestInstruments.Count == 0)
            {
                continue;
            }

            var result = await SafeFetch.RunAsync(
                () => source.FetchLatestAsync(latestInstruments, cancellationToken),
                ex => $"{group.Key} fetch threw unexpectedly: {ex.Message}");

            switch (result.Outcome)
            {
                case PriceFetchOutcome.Success:
                    var returnedIds = await QuoteUpsert.UpsertInstrumentQuotesAsync(db, result.Values, cancellationToken);
                    synced += returnedIds.Count;
                    failed += latestInstruments.Count(i => !returnedIds.Contains(i.Id));
                    break;
                case PriceFetchOutcome.NoData:
                    noData += latestInstruments.Count;
                    break;
                case PriceFetchOutcome.Error:
                    logger.LogWarning("PriceSyncJob: {Source} fetch failed: {Reason}", group.Key, result.ErrorReason);
                    failed += latestInstruments.Count;
                    break;
            }

            if (source.RequestDelay > TimeSpan.Zero)
            {
                await Task.Delay(source.RequestDelay, cancellationToken);
            }
        }

        run.Finish(timeProvider.GetUtcNow(), synced, noData, failed);

        // A Partial run still publishes, as Reporting falls back to last-known prices; only a Failed run is skipped.
        if (run.Status is SyncRunStatus.Completed or SyncRunStatus.Partial)
        {
            await publishEndpoint.Publish(new DailyPricesSynced
            {
                RunId = run.Id,
                SyncDate = DateOnly.FromDateTime(run.StartedAt.UtcDateTime),
                SyncedCount = synced,
                FailedCount = failed,
                NoDataCount = noData,
                Kind = PriceSyncKind.Prices,
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        activity?.SetTag("skarbiec.sync_run.id", run.Id);
        activity?.SetTag("skarbiec.sync_run.status", run.Status.ToString());
        activity?.SetTag("skarbiec.sync_run.synced", synced);
        activity?.SetTag("skarbiec.sync_run.no_data", noData);
        activity?.SetTag("skarbiec.sync_run.failed", failed);
        activity?.SetTag("skarbiec.sync_run.skipped", skipped);

        // Skipped is logged, not stored, so the SyncRun counters keep meaning "attempted".
        logger.LogInformation(
            "PriceSyncJob run {RunId} finished: {Status} (synced={Synced}, noData={NoData}, failed={Failed}, skipped={Skipped}).",
            run.Id, run.Status, synced, noData, failed, skipped);
    }

    // Null when the last quote is already today: nothing is missing, so the latest fetch keeps the run's value fresh.
    internal static DateOnly? ClampGapStart(DateOnly lastQuote, DateOnly today)
    {
        var from = lastQuote.AddDays(1);
        var earliest = today.AddDays(-MaxGapDays);
        from = from < earliest ? earliest : from;
        return from > today ? null : from;
    }

    private async Task<Dictionary<Guid, DateOnly>> GetLastQuoteDatesAsync(
        List<Instrument> instruments, CancellationToken cancellationToken)
    {
        var ids = instruments.Select(i => i.Id).ToList();
        return await db.PriceQuotes
            .AsNoTracking()
            .Where(q => ids.Contains(q.InstrumentId))
            .GroupBy(q => q.InstrumentId)
            .Select(g => new { InstrumentId = g.Key, Last = g.Max(q => q.Date) })
            .ToDictionaryAsync(x => x.InstrumentId, x => x.Last, cancellationToken);
    }

    // No exception for Unverified instruments: HistoryBackfillJob resolves those on creation and on first use.
    private async Task<(List<Instrument> Selected, int Skipped)> GetInstrumentsToSyncAsync(CancellationToken cancellationToken)
    {
        var selected = await db.Instruments
            .AsNoTracking()
            .Where(i => db.InstrumentUsages.Any(u => u.InstrumentId == i.Id && (u.AssetCount > 0 || u.WatchCount > 0)))
            .ToListAsync(cancellationToken);

        var total = await db.Instruments.CountAsync(cancellationToken);

        return (selected, total - selected.Count);
    }
}
