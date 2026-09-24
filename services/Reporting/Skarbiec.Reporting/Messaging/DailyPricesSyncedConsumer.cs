using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// On <see cref="DailyPricesSynced"/> (T2.10), recomputes every user's <see cref="ValuationSnapshot"/>
/// and its <see cref="AssetValuation"/> lines for every portfolio (E5, ADR-015). Positions come from
/// Reporting's own <see cref="Position"/> read model, fed by Portfolio's events (spec-03, ADR-021) —
/// the only remaining cross-service call is the prices/FX batch to MarketData's <c>/internal</c>
/// endpoints, sent with no token (ADR-027). Idempotent via the inbox template (T0.12, applied through
/// <see cref="DailyPricesSyncedConsumerDefinition"/>) plus the upsert-by-(PortfolioId, Date) and
/// -(AssetId, Date) unique indexes — a redelivery or a manual rerun overwrites the same rows.
/// Both <see cref="PriceSyncKind"/> values recompute identically (spec-04 design decision 8): a
/// same-day Prices run followed by an Fx run simply overwrites that day's rows with fresher inputs.
/// Since spec-07 it also keeps every fetched price and rate in <see cref="LatestInstrumentPrice"/> /
/// <see cref="LatestFxRate"/>, and the per-portfolio upsert lives in <see cref="PortfolioSnapshotWriter"/>,
/// shared with the position-event path that revalues today from those local rows (ADR-025).
/// </summary>
/// <remarks>
/// Isolation mirrors <c>PriceSyncJob</c> (same "one bad input doesn't stop the rest" philosophy):
/// one portfolio's own computation failing (caught below) doesn't abort the others in the same
/// message, and nothing is written to the DB for it until it succeeds — so no failed statement ever
/// reaches Postgres to poison the single ambient transaction the EF outbox wraps this consume in
/// (no savepoints are available to recover mid-transaction otherwise). A total failure to reach
/// MarketData at all is a different failure mode — there's nothing to compute for anyone — so it's
/// deliberately left to propagate and let the inbox's retry policy (<c>UseMessageRetry</c>) recover
/// once the dependency is back, same as any other transient fault.
/// </remarks>
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

        // Bypasses the tenancy filter deliberately (IgnoreQueryFilters): this consumer has no
        // single current user to filter by, and it values every user's portfolios in one pass by
        // design. Archived portfolios are excluded from valuation, same as before spec-03 — their
        // last snapshot simply stays where it was.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => !p.PortfolioIsArchived)
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

        var existingSnapshots = await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.Date == snapshotDate)
            .ToDictionaryAsync(s => s.PortfolioId, cancellationToken);

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

    /// <summary>
    /// spec-07: keeps every fetched close in <see cref="LatestInstrumentPrice"/> so the position-event
    /// path can value with it later. Only moves forward — a run returning an older quote than the
    /// stored one (a rerun for a past day) leaves the stored row alone. Staged, not saved: it commits
    /// with this consume's one <c>SaveChangesAsync</c>.
    /// </summary>
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

    /// <summary>spec-07: the <see cref="LatestFxRate"/> twin of <see cref="RememberLatestPricesAsync"/> — same forward-only rule, pairs stored in canonical uppercase.</summary>
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
