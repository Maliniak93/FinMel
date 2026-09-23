using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Daily FX sync over the <see cref="Currency"/> catalog (spec-04, ADR-023): one latest-rate fetch
/// for every catalog currency except PLN (the fixed base, ADR-008), preceded by a 12-month history
/// backfill for any currency that has no <see cref="FxRate"/> row at all yet. The only
/// <see cref="IFxRateSource"/> consumer — <see cref="PriceSyncJob"/> and <see cref="HistoryBackfillJob"/>
/// no longer touch FX (spec-04 design decision 6).
/// </summary>
/// <remarks>
/// Every currency ends the run with exactly one outcome, so the <see cref="SyncRun"/> counters add up
/// to the number of currencies attempted: a failed backfill counts it failed (the run continues for
/// the others), otherwise the latest-rate fetch decides. Chunking the backfill range to NBP's 93-day
/// limit is <c>NbpFxRateSource</c>'s concern, not this job's.
/// </remarks>
[DisallowConcurrentExecution]
public sealed class FxSyncJob(
    MarketDataDbContext db,
    IFxRateSource fxRateSource,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider,
    ILogger<FxSyncJob> logger) : IJob
{
    public static readonly JobKey Key = new("fx-sync", "market-data");

    /// <summary>Registered with OpenTelemetry tracing in Program.cs, same as <see cref="PriceSyncJob.ActivitySourceName"/>.</summary>
    public const string ActivitySourceName = "Skarbiec.MarketData.FxSyncJob";

    private const string BaseCurrency = "PLN"; // ADR-008
    private const int BackfillDays = 365; // same "min. 1 year back" as HistoryBackfillJob

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Quartz-independent entry point — lets tests drive a run directly instead of faking
    /// <see cref="IJobExecutionContext"/>, matching <see cref="PriceSyncJob.RunAsync"/>'s pattern.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("FxSyncJob.Run");

        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKind.Fx,
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var codes = await db.Currencies
            .AsNoTracking()
            .Where(c => c.Code != BaseCurrency)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);

        var backfillFailed = await BackfillNewCurrenciesAsync(codes, cancellationToken);

        var synced = 0;
        var noData = 0;
        var failed = backfillFailed.Count;

        // A currency whose first backfill failed gets no latest rate this run: one row would count as
        // "has history" and the next run would never retry its 12-month backfill.
        var latestCodes = codes.Where(c => !backfillFailed.Contains(c)).ToList();
        var latestPairs = latestCodes.Select(Pair).ToHashSet(StringComparer.Ordinal);

        if (latestCodes.Count > 0)
        {
            // One call for every code — NBP table A returns all of them in a single request.
            var result = await SafeFetch.RunAsync(
                () => fxRateSource.FetchLatestAsync(latestCodes, cancellationToken),
                ex => $"FX fetch threw unexpectedly: {ex.Message}");

            switch (result.Outcome)
            {
                case PriceFetchOutcome.Success:
                    // Filtered again here rather than trusting the source to honour the requested codes.
                    var latestRates = result.Values.Where(r => latestPairs.Contains(r.Pair)).ToList();
                    var syncedPairs = await QuoteUpsert.UpsertFxRatesAsync(db, latestRates, cancellationToken);
                    synced += latestCodes.Count(c => syncedPairs.Contains(Pair(c)));
                    failed += latestCodes.Count(c => !syncedPairs.Contains(Pair(c)));
                    break;
                case PriceFetchOutcome.NoData:
                    noData += latestCodes.Count;
                    break;
                case PriceFetchOutcome.Error:
                    logger.LogWarning("FxSyncJob: latest FX fetch failed: {Reason}", result.ErrorReason);
                    failed += latestCodes.Count;
                    break;
            }
        }

        run.Finish(timeProvider.GetUtcNow(), synced, noData, failed);

        // Same publish rule as PriceSyncJob: Completed or Partial only, enrolled on this db context's
        // outbox so the SaveChangesAsync below commits the SyncRun completion write and the
        // DailyPricesSynced outbox message in one transaction (ADR-012).
        if (run.Status is SyncRunStatus.Completed or SyncRunStatus.Partial)
        {
            await publishEndpoint.Publish(new DailyPricesSynced
            {
                RunId = run.Id,
                SyncDate = DateOnly.FromDateTime(run.StartedAt.UtcDateTime),
                SyncedCount = synced,
                FailedCount = failed,
                NoDataCount = noData,
                Kind = PriceSyncKind.Fx,
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        activity?.SetTag("skarbiec.sync_run.id", run.Id);
        activity?.SetTag("skarbiec.sync_run.status", run.Status.ToString());
        activity?.SetTag("skarbiec.sync_run.synced", synced);
        activity?.SetTag("skarbiec.sync_run.no_data", noData);
        activity?.SetTag("skarbiec.sync_run.failed", failed);

        logger.LogInformation(
            "FxSyncJob run {RunId} finished: {Status} (synced={Synced}, noData={NoData}, failed={Failed}).",
            run.Id, run.Status, synced, noData, failed);
    }

    /// <summary>Backfills a year of history for every code whose pair has no <see cref="FxRate"/> row
    /// at all, one currency at a time so one failure never stops the others. Returns the codes whose
    /// backfill failed.</summary>
    private async Task<HashSet<string>> BackfillNewCurrenciesAsync(List<string> codes, CancellationToken cancellationToken)
    {
        var pairs = codes.Select(Pair).ToList();
        var pairsWithHistory = await db.FxRates
            .AsNoTracking()
            .Where(r => pairs.Contains(r.Pair))
            .Select(r => r.Pair)
            .Distinct()
            .ToListAsync(cancellationToken);

        var to = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = to.AddDays(-BackfillDays);
        var failed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var code in codes.Where(c => !pairsWithHistory.Contains(Pair(c))))
        {
            var result = await SafeFetch.RunAsync(
                () => fxRateSource.FetchHistoryAsync(code, from, to, cancellationToken),
                ex => $"FX history fetch for {code} threw unexpectedly: {ex.Message}");

            switch (result.Outcome)
            {
                case PriceFetchOutcome.Success:
                    await QuoteUpsert.UpsertFxRatesAsync(db, result.Values, cancellationToken);
                    break;
                case PriceFetchOutcome.Error:
                    logger.LogWarning("FxSyncJob: FX history backfill for {CurrencyCode} failed: {Reason}", code, result.ErrorReason);
                    failed.Add(code);
                    break;
            }

            if (fxRateSource.RequestDelay > TimeSpan.Zero)
            {
                await Task.Delay(fxRateSource.RequestDelay, cancellationToken);
            }
        }

        return failed;
    }

    private static string Pair(string code) => code + BaseCurrency;
}
