using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// One-off backfill for a single instrument's price history (E4 [M]: min. 1 year back where the
/// source allows). Enqueued by <see cref="IHistoryBackfillTrigger"/> when an instrument is created
/// (T2.8) or enters use (spec-04); external APIs are still called only from a Quartz job (ADR-007),
/// same as <see cref="PriceSyncJob"/> — this is its one-off, per-instrument counterpart. Chunking a
/// long range is each <see cref="IPriceSource"/> implementation's own concern (T2.3-T2.5), not this
/// job's. A currency's FX history is <see cref="FxSyncJob"/>'s job, not a per-instrument side effect
/// (spec-04 design decision 6).
/// </summary>
/// <remarks>
/// Writes one <see cref="SyncRun"/> (<see cref="SyncRunKind.Backfill"/>) per run so all three jobs
/// share one run log, but publishes nothing: one instrument's history gives Reporting nothing new to
/// recompute (spec-04 design decision 7).
/// </remarks>
[DisallowConcurrentExecution]
public sealed class HistoryBackfillJob(
    MarketDataDbContext db,
    IEnumerable<IPriceSource> priceSources,
    TimeProvider timeProvider,
    ILogger<HistoryBackfillJob> logger) : IJob
{
    public const string InstrumentIdDataKey = "instrumentId";

    /// <summary>Registered with OpenTelemetry tracing in Program.cs, same as <see cref="PriceSyncJob.ActivitySourceName"/>.</summary>
    public const string ActivitySourceName = "Skarbiec.MarketData.HistoryBackfillJob";

    private const int BackfillDays = 365; // E4 [M]: "min. 1 year back"

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public Task Execute(IJobExecutionContext context)
    {
        var instrumentId = Guid.Parse(context.MergedJobDataMap.GetString(InstrumentIdDataKey)!);
        return RunAsync(instrumentId, context.CancellationToken);
    }

    /// <summary>Quartz-independent entry point — lets tests drive a run directly instead of faking
    /// <see cref="IJobExecutionContext"/>, matching <see cref="PriceSyncJob.RunAsync"/>'s pattern.</summary>
    public async Task RunAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HistoryBackfillJob.Run");
        activity?.SetTag("skarbiec.instrument.id", instrumentId);

        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKind.Backfill,
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var instrument = await db.Instruments.AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == instrumentId, cancellationToken);
        if (instrument is null)
        {
            logger.LogWarning("HistoryBackfillJob: instrument {InstrumentId} not found; skipping.", instrumentId);
            await FinishAsync(run, synced: 0, noData: 0, failed: 1, cancellationToken);
            return;
        }

        var to = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = to.AddDays(-BackfillDays);

        var source = priceSources.FirstOrDefault(s => s.Source == instrument.Source);
        if (source is null)
        {
            logger.LogWarning(
                "HistoryBackfillJob: no IPriceSource registered for {Source}; instrument {InstrumentId} not backfilled.",
                instrument.Source, instrumentId);
            await FinishAsync(run, synced: 0, noData: 0, failed: 1, cancellationToken);
            return;
        }

        var (outcome, quoteCount) = await BackfillInstrumentAsync(source, instrument, from, to, cancellationToken);

        // Only a custom instrument (Features/AddCustomInstrument, T2.8) is ever Unverified going in —
        // this is the one-off check that resolves it, based on its own ticker's fetch outcome alone.
        if (instrument.VerificationStatus == InstrumentVerificationStatus.Unverified)
        {
            var verified = outcome == PriceFetchOutcome.Success && quoteCount > 0;
            await db.Instruments
                .Where(i => i.Id == instrumentId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        i => i.VerificationStatus,
                        verified ? InstrumentVerificationStatus.Verified : InstrumentVerificationStatus.Failed),
                    cancellationToken);
        }

        await FinishAsync(
            run,
            synced: outcome == PriceFetchOutcome.Success ? 1 : 0,
            noData: outcome == PriceFetchOutcome.NoData ? 1 : 0,
            failed: outcome == PriceFetchOutcome.Error ? 1 : 0,
            cancellationToken);

        activity?.SetTag("skarbiec.sync_run.id", run.Id);
        activity?.SetTag("skarbiec.backfill.quotes", quoteCount);
        logger.LogInformation(
            "HistoryBackfillJob: instrument {InstrumentId} backfilled {QuoteCount} quote(s) for {From}..{To}.",
            instrumentId, quoteCount, from, to);
    }

    private async Task FinishAsync(SyncRun run, int synced, int noData, int failed, CancellationToken cancellationToken)
    {
        run.Finish(timeProvider.GetUtcNow(), synced, noData, failed);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(PriceFetchOutcome Outcome, int Count)> BackfillInstrumentAsync(
        IPriceSource source, Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = await SafeFetch.RunAsync(
            () => source.FetchHistoryAsync(instrument, from, to, cancellationToken),
            ex => $"{instrument.Source} history fetch for {instrument.Ticker} threw unexpectedly: {ex.Message}");

        if (result.Outcome == PriceFetchOutcome.Error)
        {
            logger.LogWarning(
                "HistoryBackfillJob: {Source} history fetch for instrument {InstrumentId} failed: {Reason}",
                instrument.Source, instrument.Id, result.ErrorReason);
            return (result.Outcome, 0);
        }

        if (result.Outcome == PriceFetchOutcome.NoData)
        {
            return (result.Outcome, 0);
        }

        await QuoteUpsert.UpsertInstrumentQuotesAsync(db, result.Values, cancellationToken);
        return (result.Outcome, result.Values.Count);
    }
}
