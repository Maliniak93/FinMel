using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Daily sync: quotes for every instrument in use, isolating per-source failures so one bad vendor
/// doesn't stop the rest (E4 [M], ADR-007). External APIs are called only from here and the other
/// Quartz jobs — <see cref="IPriceSource"/> implementations never run in a request path (enforced by
/// <c>ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions</c>). FX rates are
/// <see cref="FxSyncJob"/>'s job, not this one's (spec-04 design decision 6).
/// </summary>
/// <remarks>
/// "In use" (E4 AC: "only instruments attached to assets") is MarketData's own
/// <see cref="InstrumentUsage"/> read model, built from Portfolio's <c>AssetPositionChanged</c>/
/// <c>AssetRemoved</c> events (spec-04) — never a REST call back to Portfolio.
/// </remarks>
[DisallowConcurrentExecution]
public sealed class PriceSyncJob(
    MarketDataDbContext db,
    IEnumerable<IPriceSource> priceSources,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider,
    ILogger<PriceSyncJob> logger) : IJob
{
    public static readonly JobKey Key = new("price-sync", "market-data");

    /// <summary>Registered with OpenTelemetry tracing in Program.cs — ServiceDefaults only adds the
    /// app's own ApplicationName-named source plus MassTransit's, not this one.</summary>
    public const string ActivitySourceName = "Skarbiec.MarketData.PriceSyncJob";

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

        // Partial runs still publish (T2.10 scope): Reporting's snapshots fall back to last-known
        // prices anyway (domain valuation algorithm), so a partially-failed run is still useful
        // signal. Only a wholesale Failed run (nothing synced, nothing even came back empty) is
        // skipped — there's nothing new for Reporting to react to. IPublishEndpoint.Publish enrolls
        // the outbox row on this same db context, so the SaveChangesAsync below commits the SyncRun
        // completion write and the DailyPricesSynced outbox message in one transaction (ADR-012).
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

        // Skipped is logged, not stored: the SyncRun counters keep meaning "attempted" (spec-04
        // design decision 13).
        logger.LogInformation(
            "PriceSyncJob run {RunId} finished: {Status} (synced={Synced}, noData={NoData}, failed={Failed}, skipped={Skipped}).",
            run.Id, run.Status, synced, noData, failed, skipped);
    }

    /// <summary>Only instruments some live asset points at (<see cref="InstrumentUsage.AssetCount"/>
    /// &gt; 0). No exception for <c>Unverified</c> instruments: <see cref="HistoryBackfillJob"/> resolves
    /// those on creation and on first use, so nothing waits on this daily job for them.</summary>
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
