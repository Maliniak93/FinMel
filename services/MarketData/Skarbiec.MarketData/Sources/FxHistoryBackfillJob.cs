using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.Contracts.Events;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

[DisallowConcurrentExecution]
public sealed class FxHistoryBackfillJob(
    MarketDataDbContext db,
    IFxRateSource source,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider,
    ILogger<FxHistoryBackfillJob> logger,
    FxHistoryOptions? options = null) : IJob
{
    public const string ActivitySourceName = "Skarbiec.MarketData.FxHistoryBackfillJob";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await RunAsync(cancellationToken);

    // Quartz-independent entry point, so tests drive a run without faking IJobExecutionContext.
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("FxHistoryBackfillJob.Run");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var needDate = await FxHistoryNeed.NeedDateAsync(db, today, options, cancellationToken);
        var lagging = await FxHistoryNeed.Lagging(db, needDate).OrderBy(c => c.DisplayOrder).ToListAsync(cancellationToken);

        DateOnly? addedFrom = null;
        DateOnly? addedTo = null;
        var extended = 0;
        var failed = 0;

        foreach (var currency in lagging)
        {
            var pair = currency.Code + FxHistoryNeed.BaseCurrency;
            var oldestStored = await db.FxRates
                .Where(r => r.Pair == pair)
                .MinAsync(r => (DateOnly?)r.Date, cancellationToken);
            var to = (oldestStored ?? today.AddDays(1)).AddDays(-1);

            if (needDate > to)
            {
                currency.HistoryCoveredFrom = needDate;
                continue;
            }

            var result = await SafeFetch.RunAsync(
                () => source.FetchHistoryAsync(currency.Code, needDate, to, cancellationToken),
                ex => $"FX history fetch for {currency.Code} threw unexpectedly: {ex.Message}");

            if (result.Outcome == PriceFetchOutcome.Error)
            {
                failed++;
                logger.LogWarning(
                    "FxHistoryBackfillJob: history fetch for {CurrencyCode} failed: {Reason}", currency.Code, result.ErrorReason);
            }
            else
            {
                if (result.Outcome == PriceFetchOutcome.Success && result.Values.Count > 0)
                {
                    await QuoteUpsert.UpsertFxRatesAsync(db, result.Values, cancellationToken, save: false);
                    extended++;
                    addedFrom = addedFrom is null || needDate < addedFrom ? needDate : addedFrom;
                    addedTo = addedTo is null || to > addedTo ? to : addedTo;
                }

                currency.HistoryCoveredFrom = needDate;
            }

            if (source.RequestDelay > TimeSpan.Zero)
            {
                await Task.Delay(source.RequestDelay, cancellationToken);
            }
        }

        if (addedFrom is { } from && addedTo is { } last)
        {
            await publishEndpoint.Publish(new FxHistoryBackfilled
            {
                From = from,
                To = last,
                OccurredAtUtc = timeProvider.GetUtcNow(),
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        activity?.SetTag("skarbiec.fx_backfill.extended", extended);
        activity?.SetTag("skarbiec.fx_backfill.failed", failed);
        logger.LogInformation(
            "FxHistoryBackfillJob: {Lagging} currenc(ies) behind {NeedDate}; extended {Extended}, failed {Failed}.",
            lagging.Count, needDate, extended, failed);
    }
}
