using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Portfolio;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.Messaging;

/// <summary>
/// On <see cref="DailyPricesSynced"/> (T2.10), recomputes every user's <see cref="ValuationSnapshot"/>
/// for every portfolio (E5, ADR-015). Idempotent via the inbox template (T0.12, applied through
/// <see cref="DailyPricesSyncedConsumerDefinition"/>) plus the upsert-by-(PortfolioId, Date) unique
/// index — a redelivered event or a manual rerun overwrites the same rows instead of duplicating
/// them. Runs with no caller JWT (background consumer) and needs every user's data, not one — see
/// <c>SystemCaller</c> for the trust boundary the Portfolio/MarketData clients authenticate through.
/// </summary>
/// <remarks>
/// Isolation mirrors <c>PriceSyncJob</c> (same "one bad input doesn't stop the rest" philosophy):
/// one portfolio's own computation failing (caught below) doesn't abort the others in the same
/// message, and nothing is written to the DB for it until it succeeds — so no failed statement ever
/// reaches Postgres to poison the single ambient transaction the EF outbox wraps this consume in
/// (no savepoints are available to recover mid-transaction otherwise). A total failure to reach
/// Portfolio or MarketData at all is a different failure mode — there's nothing to compute for
/// anyone — so it's deliberately left to propagate and let the inbox's retry policy
/// (<c>UseMessageRetry</c>) recover once the dependency is back, same as any other transient fault.
/// </remarks>
public sealed class DailyPricesSyncedConsumer(
    ReportingDbContext db,
    IPositionsClient positionsClient,
    IPriceQuoteClient priceQuoteClient,
    ILogger<DailyPricesSyncedConsumer> logger) : IConsumer<DailyPricesSynced>
{
    private const string BaseCurrency = "PLN"; // ADR-008

    public async Task Consume(ConsumeContext<DailyPricesSynced> context)
    {
        var snapshotDate = context.Message.SyncDate;
        var cancellationToken = context.CancellationToken;

        var positions = await positionsClient.GetAllPositionsAsync(cancellationToken);
        if (positions.Count == 0)
        {
            logger.LogInformation("DailyPricesSyncedConsumer: no positions to value for {SnapshotDate}.", snapshotDate);
            return;
        }

        var pricesByInstrument = await FetchPricesAsync(positions, snapshotDate, cancellationToken);
        var fxRatesByPair = await FetchFxRatesAsync(positions, pricesByInstrument, snapshotDate, cancellationToken);

        // Bypasses the tenancy filter deliberately (IgnoreQueryFilters): this consumer has no
        // single current user to filter by, and it's upserting across every user's portfolios in
        // one pass by design.
        var existingSnapshots = await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.Date == snapshotDate)
            .ToDictionaryAsync(s => s.PortfolioId, cancellationToken);

        var computed = 0;
        var failed = 0;

        foreach (var group in positions.GroupBy(p => p.PortfolioId))
        {
            try
            {
                UpsertSnapshot(group.Key, group.First().UserId, group.ToList(), pricesByInstrument, fxRatesByPair, snapshotDate, existingSnapshots);
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

    private void UpsertSnapshot(
        Guid portfolioId,
        Guid userId,
        IReadOnlyList<PositionForValuation> portfolioPositions,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate,
        IReadOnlyDictionary<Guid, ValuationSnapshot> existingSnapshots)
    {
        var positions = portfolioPositions
            .Select(p => new ValuationPosition
            {
                AssetClass = p.AssetClass,
                Currency = p.Currency,
                Quantity = p.Quantity,
                InstrumentId = p.InstrumentId,
                ManualValueAmount = p.ManualValueAmount,
            })
            .ToList();

        var result = ValuationAlgorithm.Calculate(positions, pricesByInstrument, fxRatesByPair, snapshotDate);
        var breakdownJson = ValuationBreakdown.Serialize(result.Breakdown);

        if (existingSnapshots.TryGetValue(portfolioId, out var snapshot))
        {
            snapshot.TotalPln = result.TotalPln;
            snapshot.BreakdownJson = breakdownJson;
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
                BreakdownJson = breakdownJson,
                IsStale = result.IsStale,
            });
        }
    }

    private async Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> FetchPricesAsync(
        IReadOnlyList<PositionForValuation> positions, DateOnly snapshotDate, CancellationToken cancellationToken)
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
        IReadOnlyList<PositionForValuation> positions,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        // Every non-PLN currency actually in play: a market asset's own quote currency (not its
        // Asset.Currency — the price is denominated in whatever the instrument quotes in), or a
        // manual asset's own Currency.
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
