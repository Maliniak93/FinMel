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

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) => await RunAsync(cancellationToken);

    // Quartz-independent entry point, so tests drive a run without faking IJobExecutionContext.
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("PriceSyncJob.Run");

        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKind.Prices,
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var (instruments, skipped) = await GetInstrumentsToSyncAsync(cancellationToken);
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

            var result = await SafeFetch.RunAsync(
                () => source.FetchLatestAsync(groupInstruments, cancellationToken),
                ex => $"{group.Key} fetch threw unexpectedly: {ex.Message}");

            switch (result.Outcome)
            {
                case PriceFetchOutcome.Success:
                    var returnedIds = await QuoteUpsert.UpsertInstrumentQuotesAsync(db, result.Values, cancellationToken);
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

    // No exception for Unverified instruments: HistoryBackfillJob resolves those on creation and on first use.
    private async Task<(List<Instrument> Selected, int Skipped)> GetInstrumentsToSyncAsync(CancellationToken cancellationToken)
    {
        var selected = await db.Instruments
            .AsNoTracking()
            .Where(i => db.InstrumentUsages.Any(u => u.InstrumentId == i.Id && u.AssetCount > 0))
            .ToListAsync(cancellationToken);

        var total = await db.Instruments.CountAsync(cancellationToken);

        return (selected, total - selected.Count);
    }
}
