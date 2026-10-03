using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.MfBonds;

// The catalog changes only when the whole file was fetched and parsed: a failure leaves every series as it was.
[DisallowConcurrentExecution]
public sealed class BondCatalogSyncJob(
    MarketDataDbContext db,
    IMfBondSource source,
    TimeProvider timeProvider,
    ILogger<BondCatalogSyncJob> logger) : IJob
{
    public static readonly JobKey Key = new("bond-catalog-sync", "market-data");

    public const string ActivitySourceName = "Skarbiec.MarketData.BondCatalogSyncJob";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) => await RunAsync(cancellationToken);

    // Quartz-independent entry point, so tests drive a run without faking IJobExecutionContext.
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("BondCatalogSyncJob.Run");

        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKind.BondCatalog,
            StartedAt = timeProvider.GetUtcNow(),
            Status = SyncRunStatus.Running,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var file = await source.FetchFileAsync(cancellationToken);
            using var stream = new MemoryStream(file);
            var parsed = MfBondFileParser.Parse(stream, logger);
            if (parsed.Count == 0)
            {
                throw new InvalidDataException("The MF bond file has no series of a current bond type.");
            }

            var changed = await UpsertAsync(parsed, cancellationToken);

            // One save: the catalog and the Completed run commit in the same transaction.
            run.Finish(timeProvider.GetUtcNow(), synced: parsed.Count, noData: 0, failed: 0);
            await db.SaveChangesAsync(cancellationToken);

            activity?.SetTag("skarbiec.sync_run.changed", changed);
            logger.LogInformation(
                "BondCatalogSyncJob run {RunId} finished: {Status} (series={Series}, changed={Changed}).",
                run.Id, run.Status, parsed.Count, changed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            db.SyncRuns.Attach(run);
            run.Finish(timeProvider.GetUtcNow(), synced: 0, noData: 0, failed: 1);
            await db.SaveChangesAsync(cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "BondCatalogSyncJob run {RunId} failed: {Reason}", run.Id, ex.Message);
        }

        activity?.SetTag("skarbiec.sync_run.id", run.Id);
        activity?.SetTag("skarbiec.sync_run.status", run.Status.ToString());
    }

    // Upserts by code; a series missing from the file is kept, and a series' period rates become exactly the file's.
    private async Task<int> UpsertAsync(IReadOnlyList<ParsedBondSeries> parsed, CancellationToken cancellationToken)
    {
        var existing = await db.BondSeries
            .Include(s => s.PeriodRates)
            .ToDictionaryAsync(s => s.Code, StringComparer.Ordinal, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var changed = 0;

        foreach (var item in parsed.DistinctBy(s => s.Code))
        {
            if (!existing.TryGetValue(item.Code, out var series))
            {
                series = new BondSeries
                {
                    Code = item.Code,
                    Type = item.Type,
                    Isin = item.Isin,
                    SaleStart = item.SaleStart,
                    SaleEnd = item.SaleEnd,
                    IssuePrice = item.IssuePrice,
                    UpdatedAtUtc = now,
                };
                db.BondSeries.Add(series);
            }

            if (Apply(series, item))
            {
                series.UpdatedAtUtc = now;
                changed++;
            }
        }

        return changed;
    }

    private static bool Apply(BondSeries series, ParsedBondSeries item)
    {
        var changed = series.Type != item.Type
            || series.Isin != item.Isin
            || series.SaleStart != item.SaleStart
            || series.SaleEnd != item.SaleEnd
            || series.IssuePrice != item.IssuePrice
            || series.SwapPrice != item.SwapPrice
            || series.MarginPercent != item.MarginPercent;

        series.Type = item.Type;
        series.Isin = item.Isin;
        series.SaleStart = item.SaleStart;
        series.SaleEnd = item.SaleEnd;
        series.IssuePrice = item.IssuePrice;
        series.SwapPrice = item.SwapPrice;
        series.MarginPercent = item.MarginPercent;

        var rates = series.PeriodRates.ToDictionary(r => r.PeriodIndex);
        for (var index = 0; index < item.PeriodRates.Count; index++)
        {
            var ratePercent = item.PeriodRates[index];
            if (!rates.Remove(index, out var rate))
            {
                series.PeriodRates.Add(new BondSeriesPeriodRate { SeriesCode = series.Code, PeriodIndex = index, RatePercent = ratePercent });
                changed = true;
            }
            else if (rate.RatePercent != ratePercent)
            {
                rate.RatePercent = ratePercent;
                changed = true;
            }
        }

        foreach (var stale in rates.Values)
        {
            series.PeriodRates.Remove(stale);
            changed = true;
        }

        return changed;
    }
}
