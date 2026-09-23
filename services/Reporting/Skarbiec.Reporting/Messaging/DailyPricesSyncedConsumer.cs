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
/// the only remaining cross-service call is the prices/FX batch to MarketData, authenticated with a
/// <c>SystemCaller</c> token because a message-driven consumer has no caller JWT to forward and
/// needs every user's data, not one. Idempotent via the inbox template (T0.12, applied through
/// <see cref="DailyPricesSyncedConsumerDefinition"/>) plus the upsert-by-(PortfolioId, Date) and
/// -(AssetId, Date) unique indexes — a redelivery or a manual rerun overwrites the same rows.
/// Both <see cref="PriceSyncKind"/> values recompute identically (spec-04 design decision 8): a
/// same-day Prices run followed by an Fx run simply overwrites that day's rows with fresher inputs.
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
    ILogger<DailyPricesSyncedConsumer> logger) : IConsumer<DailyPricesSynced>
{
    private const string BaseCurrency = "PLN"; // ADR-008

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
                UpsertPortfolio(group.Key, [.. group], pricesByInstrument, fxRatesByPair, snapshotDate, existingSnapshots, existingLines);
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

    private void UpsertPortfolio(
        Guid portfolioId,
        IReadOnlyList<Position> portfolioPositions,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate,
        IReadOnlyDictionary<Guid, ValuationSnapshot> existingSnapshots,
        IReadOnlyDictionary<Guid, AssetValuation> existingLines)
    {
        var userId = portfolioPositions[0].UserId;

        var valuationPositions = portfolioPositions
            .Select(p => new ValuationPosition
            {
                AssetId = p.AssetId,
                AssetClass = p.AssetClass,
                ValuationMode = p.ValuationMode,
                Currency = p.Currency,
                Quantity = p.Quantity,
                InstrumentId = p.InstrumentId,
                ManualValueAmount = p.ManualValueAmount,
            })
            .ToList();

        var result = ValuationAlgorithm.Calculate(valuationPositions, pricesByInstrument, fxRatesByPair, snapshotDate);

        foreach (var valued in result.Lines)
        {
            UpsertLine(portfolioId, userId, valued, snapshotDate, existingLines);
        }

        if (existingSnapshots.TryGetValue(portfolioId, out var snapshot))
        {
            snapshot.UserId = userId;
            snapshot.TotalPln = result.TotalPln;
            snapshot.IsStale = result.IsStale;
        }
        else
        {
            db.ValuationSnapshots.Add(new ValuationSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PortfolioId = portfolioId,
                Date = snapshotDate,
                TotalPln = result.TotalPln,
                IsStale = result.IsStale,
            });
        }
    }

    private void UpsertLine(
        Guid portfolioId,
        Guid userId,
        ValuedPosition valued,
        DateOnly snapshotDate,
        IReadOnlyDictionary<Guid, AssetValuation> existingLines)
    {
        if (existingLines.TryGetValue(valued.AssetId, out var line))
        {
            line.UserId = userId;
            line.AssetClass = valued.AssetClass;
            line.Quantity = valued.Quantity;
            line.PriceUsed = valued.PriceUsed;
            line.PriceDate = valued.PriceDate;
            line.FxRateUsed = valued.FxRateUsed;
            line.ValuePln = valued.ValuePln;
            line.IsStale = valued.IsStale;
            return;
        }

        db.AssetValuations.Add(new AssetValuation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PortfolioId = portfolioId,
            AssetId = valued.AssetId,
            Date = snapshotDate,
            AssetClass = valued.AssetClass,
            Quantity = valued.Quantity,
            PriceUsed = valued.PriceUsed,
            PriceDate = valued.PriceDate,
            FxRateUsed = valued.FxRateUsed,
            ValuePln = valued.ValuePln,
            IsStale = valued.IsStale,
        });
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
        // Every non-PLN currency actually in play: a market asset's own quote currency (not its
        // Asset.Currency — the price is denominated in whatever the instrument quotes in), or a
        // manual/currency-valued asset's own Currency (M1.4: both non-market modes key off Currency,
        // so filtering on InstrumentId is null already covers the new mode with no change here).
        var currencies = pricesByInstrument.Values.Select(p => p.QuoteCurrency)
            .Concat(positions.Where(p => p.InstrumentId is null).Select(p => p.Currency))
            .Where(c => !string.Equals(c, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToUpperInvariant() + BaseCurrency)
            .Distinct()
            .ToList();

        return currencies.Count == 0
            ? new Dictionary<string, FxRateLookup>()
            : await priceQuoteClient.GetLatestFxRatesAsync(currencies, snapshotDate, cancellationToken);
    }
}
