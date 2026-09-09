using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
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
    IPublishEndpoint publishEndpoint,
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

        // M1.4: union with every user-selectable currency (Skarbiec.Contracts.SupportedCurrencies),
        // not just the currencies today's instruments happen to quote in. Before this, a currency
        // with no instrument behind it (EUR: nothing quotes in it; the seeded USD coverage was
        // incidental to three instruments) never got a daily rate at all — a currency-valued asset
        // (M1.4's third valuation mode) in that currency would value off MarketDataSeeder's 2020
        // bootstrap rate forever, flagged stale but never corrected. NBP table A returns every
        // published currency in one call regardless of what's asked for (NbpFxRateSource's own doc
        // comment), so this costs no extra HTTP request — it only changes which rows land.
        var instrumentCurrencies = new HashSet<string>(
            instruments.Select(i => i.QuoteCurrency).Where(c => !string.Equals(c, BaseCurrency, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var currencies = instrumentCurrencies
            .Concat(SupportedCurrencies.All)
            .Where(c => !string.Equals(c, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // currencies is never empty now (SupportedCurrencies.All always has EUR/USD beyond PLN), so
        // the FX branch always runs — it's no longer conditional on instrument coverage.
        var fxResult = await SafeFetch.RunAsync(
            () => fxRateSource.FetchLatestAsync(currencies, cancellationToken),
            ex => $"FX fetch threw unexpectedly: {ex.Message}");

        switch (fxResult.Outcome)
        {
            case PriceFetchOutcome.Success:
                var syncedPairs = await QuoteUpsert.UpsertFxRatesAsync(db, fxResult.Values, cancellationToken);
                var syncedCurrencies = currencies.Count(c => syncedPairs.Contains(c.ToUpperInvariant() + BaseCurrency));
                synced += syncedCurrencies;

                // FailedCount decision (M1.4): a supported-set currency nobody currently holds an
                // instrument in (e.g. EUR before any EUR instrument/asset exists) doesn't count as a
                // failure just because NBP's response didn't happen to include it — MarketData can't
                // see Portfolio's assets (ADR-003, no cross-DB joins) to know if it's actually "in
                // use" beyond instrument coverage, and failing closed on a currency nobody's
                // valuation depends on today is noise that would make a routine run look Partial. A
                // currency an instrument actually quotes in still counts exactly as before — that one
                // *is* a real signal something's wrong.
                var missingInstrumentBacked = currencies
                    .Where(c => !syncedPairs.Contains(c.ToUpperInvariant() + BaseCurrency))
                    .Count(instrumentCurrencies.Contains);
                failed += missingInstrumentBacked;
                break;
            case PriceFetchOutcome.NoData:
                noData += currencies.Count;
                break;
            case PriceFetchOutcome.Error:
                logger.LogWarning("PriceSyncJob: FX fetch failed: {Reason}", fxResult.ErrorReason);
                // Same instrument-backed scoping as the Success branch above — a total FX outage is
                // still only a *failure* for currencies something actually depends on today.
                failed += currencies.Count(instrumentCurrencies.Contains);
                break;
        }

        run.FinishedAt = timeProvider.GetUtcNow();
        run.SyncedCount = synced;
        run.NoDataCount = noData;
        run.FailedCount = failed;
        run.Status = DetermineStatus(synced, noData, failed);

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
            }, cancellationToken);
        }

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
}
