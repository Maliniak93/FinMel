using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

// A failed portfolio stays out of the database so it never poisons the outbox transaction; failing to reach MarketData propagates for the retry.
public sealed class DailyPricesSyncedConsumer(
    ReportingDbContext db,
    IPriceQuoteClient priceQuoteClient,
    PortfolioSnapshotWriter snapshotWriter,
    ILogger<DailyPricesSyncedConsumer> logger) : IConsumer<DailyPricesSynced>
{
    public async Task Consume(ConsumeContext<DailyPricesSynced> context)
    {
        var snapshotDate = context.Message.SyncDate;
        var cancellationToken = context.CancellationToken;

        // IgnoreQueryFilters: this consumer values every user's portfolios in one pass; archived ones are excluded.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => !p.PortfolioIsArchived && !p.IsArchived)
            .ToListAsync(cancellationToken);

        if (positions.Count == 0)
        {
            logger.LogInformation("DailyPricesSyncedConsumer: no positions to value for {SnapshotDate}.", snapshotDate);
            return;
        }

        var pricesByInstrument = await FetchPricesAsync(positions, snapshotDate, cancellationToken);
        var fxRatesByPair = await FetchFxRatesAsync(positions, pricesByInstrument, snapshotDate, cancellationToken);

        await RememberLatestPricesAsync(pricesByInstrument, cancellationToken);
        await RememberLatestFxRatesAsync(fxRatesByPair, cancellationToken);

        // IgnoreQueryFilters: the sync date's snapshots of every user are upserted in this one pass.
        var existingSnapshots = await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.Date == snapshotDate)
            .ToDictionaryAsync(s => s.PortfolioId, cancellationToken);

        // IgnoreQueryFilters: the sync date's lines of every user are upserted in this one pass.
        var existingLines = await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.Date == snapshotDate)
            .ToDictionaryAsync(l => l.AssetId, cancellationToken);

        var computed = 0;
        var failed = 0;

        foreach (var group in positions.GroupBy(p => p.PortfolioId))
        {
            try
            {
                List<Position> portfolioPositions = [.. group];
                snapshotWriter.UpsertPortfolio(
                    group.Key,
                    portfolioPositions[0].UserId,
                    portfolioPositions,
                    pricesByInstrument,
                    fxRatesByPair,
                    snapshotDate,
                    existingSnapshots.GetValueOrDefault(group.Key),
                    existingLines);
                computed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DailyPricesSyncedConsumer: valuation failed for portfolio {PortfolioId}.", group.Key);
                failed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "DailyPricesSyncedConsumer: snapshot run for {SnapshotDate} finished: {Computed} portfolio(s) computed, {Failed} failed.",
            snapshotDate, computed, failed);
    }

    private async Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> FetchPricesAsync(
        IReadOnlyList<Position> positions, DateOnly snapshotDate, CancellationToken cancellationToken)
    {
        var instrumentIds = positions
            .Where(p => p.InstrumentId is { })
            .Select(p => p.InstrumentId!.Value)
            .Distinct()
            .ToList();

        return instrumentIds.Count == 0
            ? new Dictionary<Guid, InstrumentPriceLookup>()
            : await priceQuoteClient.GetLatestPricesAsync(instrumentIds, snapshotDate, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, FxRateLookup>> FetchFxRatesAsync(
        IReadOnlyList<Position> positions,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        var pairs = PortfolioSnapshotWriter.RequiredFxPairs(positions, pricesByInstrument);

        return pairs.Count == 0
            ? new Dictionary<string, FxRateLookup>()
            : await priceQuoteClient.GetLatestFxRatesAsync(pairs, snapshotDate, cancellationToken);
    }

    // Only moves forward: an older quote from a rerun leaves the stored row alone.
    private async Task RememberLatestPricesAsync(
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument, CancellationToken cancellationToken)
    {
        if (pricesByInstrument.Count == 0)
        {
            return;
        }

        var instrumentIds = pricesByInstrument.Keys.ToList();
        var stored = await db.LatestInstrumentPrices
            .Where(p => instrumentIds.Contains(p.InstrumentId))
            .ToDictionaryAsync(p => p.InstrumentId, cancellationToken);

        foreach (var (instrumentId, price) in pricesByInstrument)
        {
            if (!stored.TryGetValue(instrumentId, out var row))
            {
                db.LatestInstrumentPrices.Add(new LatestInstrumentPrice
                {
                    InstrumentId = instrumentId,
                    QuoteCurrency = price.QuoteCurrency,
                    Date = price.Date,
                    Close = price.Close,
                });
            }
            else if (price.Date >= row.Date)
            {
                row.QuoteCurrency = price.QuoteCurrency;
                row.Date = price.Date;
                row.Close = price.Close;
            }
        }
    }

    // The same forward-only rule, with pairs in canonical uppercase.
    private async Task RememberLatestFxRatesAsync(
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair, CancellationToken cancellationToken)
    {
        if (fxRatesByPair.Count == 0)
        {
            return;
        }

        var pairs = fxRatesByPair.Keys.Select(p => p.ToUpperInvariant()).Distinct().ToList();
        var stored = await db.LatestFxRates
            .Where(r => pairs.Contains(r.Pair))
            .ToDictionaryAsync(r => r.Pair, cancellationToken);

        foreach (var (key, rate) in fxRatesByPair)
        {
            var pair = key.ToUpperInvariant();
            if (!stored.TryGetValue(pair, out var row))
            {
                stored[pair] = db.LatestFxRates.Add(new LatestFxRate
                {
                    Pair = pair,
                    Date = rate.Date,
                    Rate = rate.Rate,
                }).Entity;
            }
            else if (rate.Date >= row.Date)
            {
                row.Date = rate.Date;
                row.Rate = rate.Rate;
            }
        }
    }
}
