using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// Holds the portfolio's advisory lock across the MarketData calls; a failure there propagates and rolls everything back, request included.
public sealed class PortfolioHistoryRebuildConsumer(
    ReportingDbContext db,
    IPriceQuoteClient priceQuoteClient,
    TimeProvider timeProvider,
    ILogger<PortfolioHistoryRebuildConsumer> logger) : IConsumer<PortfolioHistoryRebuildRequested>
{
    private const string RevisionGuardSavepoint = "revision_guard";

    public async Task Consume(ConsumeContext<PortfolioHistoryRebuildRequested> context)
    {
        var portfolioId = context.Message.PortfolioId;
        var cancellationToken = context.CancellationToken;

        await PortfolioSnapshotWriter.LockPortfolioAsync(db, portfolioId, cancellationToken);

        // IgnoreQueryFilters: a consumer has no request user and rewrites the portfolio of the user the request names.
        var request = await db.HistoryRebuildRequests
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.PortfolioId == portfolioId, cancellationToken);

        if (request is null)
        {
            logger.LogInformation("PortfolioHistoryRebuildConsumer: no pending rebuild for portfolio {PortfolioId}.", portfolioId);
            return;
        }

        var from = request.FromDate;
        var to = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-1);

        if (from <= to)
        {
            await RewriteAsync(request, from, to, cancellationToken);
        }

        await DeleteRequestAtRevisionAsync(request, cancellationToken);
    }

    // Deleted only at the revision read above: a request committed meanwhile keeps the row and its own message rebuilds again.
    private async Task DeleteRequestAtRevisionAsync(HistoryRebuildRequest request, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(RevisionGuardSavepoint, cancellationToken);
        }

        try
        {
            await db.HistoryRebuildRequests
                .IgnoreQueryFilters()
                .Where(r => r.PortfolioId == request.PortfolioId && r.Revision == request.Revision)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception) when (transaction is not null && IsSerializationFailure(exception))
        {
            // The inbox transaction is RepeatableRead: a row bumped since its snapshot fails instead of not matching, and the savepoint keeps the rebuild.
            await transaction.RollbackToSavepointAsync(RevisionGuardSavepoint, cancellationToken);
        }
    }

    private async Task RewriteAsync(HistoryRebuildRequest request, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var portfolioId = request.PortfolioId;

        // IgnoreQueryFilters: the positions belong to the request's user, not to a request user.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId)
            .ToListAsync(cancellationToken);

        var instrumentIds = positions
            .Where(p => p.InstrumentId is { })
            .Select(p => p.InstrumentId!.Value)
            .Distinct()
            .ToList();
        var priceSeries = instrumentIds.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<InstrumentPriceLookup>>()
            : await priceQuoteClient.GetPriceHistoryAsync(instrumentIds, from, to, cancellationToken);

        var quoteCurrencies = priceSeries
            .Where(s => s.Value.Count > 0)
            .ToDictionary(s => s.Key, s => s.Value[0]);
        var pairs = PortfolioSnapshotWriter.RequiredFxPairs(positions, quoteCurrencies);
        var fxSeries = pairs.Count == 0
            ? new Dictionary<string, IReadOnlyList<FxRateLookup>>()
            : await priceQuoteClient.GetFxHistoryAsync(pairs, from, to, cancellationToken);

        var manualAssetIds = positions
            .Where(p => p.ValuationMode == AssetValuationMode.Manual)
            .Select(p => p.AssetId)
            .ToList();

        // IgnoreQueryFilters: the stored Manual lines belong to the request's user, not to a request user.
        var storedManualLines = manualAssetIds.Count == 0
            ? []
            : await db.AssetValuations
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(l => l.PortfolioId == portfolioId && manualAssetIds.Contains(l.AssetId) && l.Date >= from && l.Date <= to)
                .ToListAsync(cancellationToken);

        var days = HistoryRebuild.Compute(positions, priceSeries, fxSeries, from, to, storedManualLines);

        // IgnoreQueryFilters: the rows being replaced belong to the request's user, not to a request user.
        await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId && l.Date >= from && l.Date <= to)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolioId && s.Date >= from && s.Date <= to)
            .ExecuteDeleteAsync(cancellationToken);

        db.AssetValuations.AddRange(days.SelectMany(day => day.Lines.Select(line => new AssetValuation
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            PortfolioId = portfolioId,
            AssetId = line.AssetId,
            Date = day.Date,
            AssetClass = line.AssetClass,
            Quantity = line.Quantity,
            PriceUsed = line.PriceUsed,
            PriceDate = line.PriceDate,
            FxRateUsed = line.FxRateUsed,
            ValuePln = line.ValuePln,
            IsStale = line.IsStale,
        })));

        db.ValuationSnapshots.AddRange(days.Select(day => new ValuationSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            PortfolioId = portfolioId,
            Date = day.Date,
            TotalPln = day.TotalPln,
            IsStale = day.IsStale,
        }));

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "PortfolioHistoryRebuildConsumer: rebuilt {Days} day(s) of portfolio {PortfolioId} from {From} to {To}.",
            days.Count, portfolioId, from, to);
    }

    // The Npgsql execution strategy wraps the transient PostgresException in an InvalidOperationException.
    private static bool IsSerializationFailure(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.SerializationFailure;
}
