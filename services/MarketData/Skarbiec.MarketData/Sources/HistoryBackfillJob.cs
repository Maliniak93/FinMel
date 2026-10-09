using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using Quartz;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

[DisallowConcurrentExecution]
public sealed class HistoryBackfillJob(
    MarketDataDbContext db,
    IEnumerable<IPriceSource> priceSources,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider,
    ILogger<HistoryBackfillJob> logger) : IJob
{
    public const string InstrumentIdDataKey = "instrumentId";

    public const string FromDataKey = "from";

    public const string ActivitySourceName = "Skarbiec.MarketData.HistoryBackfillJob";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var instrumentId = Guid.Parse(context.MergedJobDataMap.GetString(InstrumentIdDataKey)!);
        var from = DateOnly.ParseExact(context.MergedJobDataMap.GetString(FromDataKey)!, "O", CultureInfo.InvariantCulture);
        await RunAsync(instrumentId, from, cancellationToken);
    }

    // Quartz-independent entry point, so tests drive a run without faking IJobExecutionContext.
    public async Task RunAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HistoryBackfillJob.Run");
        activity?.SetTag("skarbiec.instrument.id", instrumentId);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var instrument = await db.Instruments.AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == instrumentId, cancellationToken);

        // Only the stretch before what is already covered is fetched; the daily sync owns everything after.
        var to = instrument?.HistoryCoveredFrom?.AddDays(-1) ?? today;
        if (instrument is not null && from > to)
        {
            logger.LogInformation(
                "HistoryBackfillJob: instrument {InstrumentId} already covered from {CoveredFrom}; nothing to backfill from {From}.",
                instrumentId, instrument.HistoryCoveredFrom, from);
            return;
        }

        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKind.Backfill,
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        if (instrument is null)
        {
            logger.LogWarning("HistoryBackfillJob: instrument {InstrumentId} not found; skipping.", instrumentId);
            await FinishAsync(run, synced: 0, noData: 0, failed: 1, cancellationToken);
            return;
        }

        var source = priceSources.FirstOrDefault(s => s.Source == instrument.Source);
        if (source is null)
        {
            logger.LogWarning(
                "HistoryBackfillJob: no IPriceSource registered for {Source}; instrument {InstrumentId} not backfilled.",
                instrument.Source, instrumentId);
            await FinishAsync(run, synced: 0, noData: 0, failed: 1, cancellationToken);
            return;
        }

        var fetchFrom = source.MaxHistoryDays is { } maxDays && from < today.AddDays(-maxDays)
            ? today.AddDays(-maxDays)
            : from;

        var (outcome, quoteCount) = fetchFrom > to
            ? (PriceFetchOutcome.NoData, 0)
            : await BackfillInstrumentAsync(source, instrument, fetchFrom, to, cancellationToken);

        // The requested date, not the clamped one: a source that cannot reach it must not be re-enqueued forever.
        // Quotes, coverage, verification, the event and the SyncRun all commit in the FinishAsync save below.
        var tracked = await db.Instruments.SingleAsync(i => i.Id == instrumentId, cancellationToken);
        if (outcome != PriceFetchOutcome.Error && (tracked.HistoryCoveredFrom is null || tracked.HistoryCoveredFrom > from))
        {
            tracked.HistoryCoveredFrom = from;
        }

        // Only a custom instrument is ever Unverified here; this run resolves it from its own fetch outcome.
        if (tracked.VerificationStatus == InstrumentVerificationStatus.Unverified)
        {
            var verified = outcome == PriceFetchOutcome.Success && quoteCount > 0;
            tracked.VerificationStatus = verified ? InstrumentVerificationStatus.Verified : InstrumentVerificationStatus.Failed;
        }

        if (outcome == PriceFetchOutcome.Success)
        {
            await publishEndpoint.Publish(new InstrumentHistoryBackfilled
            {
                InstrumentId = instrumentId,
                From = fetchFrom,
                To = to,
                OccurredAtUtc = timeProvider.GetUtcNow(),
            }, cancellationToken);
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
            instrumentId, quoteCount, fetchFrom, to);
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

        await QuoteUpsert.UpsertInstrumentQuotesAsync(db, result.Values, cancellationToken, save: false);
        return (result.Outcome, result.Values.Count);
    }
}
