using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// One-off backfill for a single instrument's price history (E4 [M]: min. 1 year back where the
/// source allows) plus, when the instrument isn't PLN-quoted, the matching FX pair's history — a USD
/// instrument needs a year of USD/PLN for its history chart to be honest (T2.7 scope). Enqueued by
/// <see cref="IHistoryBackfillTrigger"/> when an instrument enters use; external APIs are still called
/// only from a Quartz job (ADR-007), same as <see cref="PriceSyncJob"/> — this is its one-off,
/// per-instrument counterpart. Chunking a long range is each <see cref="IPriceSource"/>/
/// <see cref="IFxRateSource"/> implementation's own concern (T2.3-T2.5), not this job's.
/// </summary>
/// <remarks>
/// Nothing calls <see cref="IHistoryBackfillTrigger.EnqueueAsync"/> yet — the two real trigger points
/// (a custom instrument's creation, T2.8; an instrument's first attach to an asset, T2.9) don't exist
/// as request handlers until those tasks land. Wire the call in from there once they do, the same way
/// T2.6 documented deferring its "in use" filter to T2.9.
/// </remarks>
[DisallowConcurrentExecution]
public sealed class HistoryBackfillJob(
    MarketDataDbContext db,
    IEnumerable<IPriceSource> priceSources,
    IFxRateSource fxRateSource,
    TimeProvider timeProvider,
    ILogger<HistoryBackfillJob> logger) : IJob
{
    public const string InstrumentIdDataKey = "instrumentId";

    /// <summary>Registered with OpenTelemetry tracing in Program.cs, same as <see cref="PriceSyncJob.ActivitySourceName"/>.</summary>
    public const string ActivitySourceName = "Skarbiec.MarketData.HistoryBackfillJob";

    private const string BaseCurrency = "PLN"; // ADR-008
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

        var instrument = await db.Instruments.AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == instrumentId, cancellationToken);
        if (instrument is null)
        {
            logger.LogWarning("HistoryBackfillJob: instrument {InstrumentId} not found; skipping.", instrumentId);
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
            return;
        }

        var quoteCount = await BackfillInstrumentAsync(source, instrument, from, to, cancellationToken);

        var fxCount = 0;
        if (!string.Equals(instrument.QuoteCurrency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            fxCount = await BackfillFxAsync(instrument.QuoteCurrency, from, to, cancellationToken);
        }

        activity?.SetTag("skarbiec.backfill.quotes", quoteCount);
        activity?.SetTag("skarbiec.backfill.fx_rates", fxCount);
        logger.LogInformation(
            "HistoryBackfillJob: instrument {InstrumentId} backfilled {QuoteCount} quote(s), {FxCount} FX rate(s) for {From}..{To}.",
            instrumentId, quoteCount, fxCount, from, to);
    }

    private async Task<int> BackfillInstrumentAsync(
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
            return 0;
        }

        if (result.Outcome == PriceFetchOutcome.NoData)
        {
            return 0;
        }

        await QuoteUpsert.UpsertInstrumentQuotesAsync(db, result.Values, cancellationToken);
        return result.Values.Count;
    }

    private async Task<int> BackfillFxAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = await SafeFetch.RunAsync(
            () => fxRateSource.FetchHistoryAsync(currencyCode, from, to, cancellationToken),
            ex => $"FX history fetch for {currencyCode} threw unexpectedly: {ex.Message}");

        if (result.Outcome == PriceFetchOutcome.Error)
        {
            logger.LogWarning("HistoryBackfillJob: FX history fetch for {CurrencyCode} failed: {Reason}", currencyCode, result.ErrorReason);
            return 0;
        }

        if (result.Outcome == PriceFetchOutcome.NoData)
        {
            return 0;
        }

        await QuoteUpsert.UpsertFxRatesAsync(db, result.Values, cancellationToken);
        return result.Values.Count;
    }
}
