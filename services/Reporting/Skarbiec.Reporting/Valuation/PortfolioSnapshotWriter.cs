using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

/// <summary>
/// The one place a portfolio's valuation turns into <see cref="AssetValuation"/> lines and a
/// <see cref="ValuationSnapshot"/> (spec-07 design decision 6). Two callers: the daily
/// <c>DailyPricesSyncedConsumer</c>, which stages every portfolio for the sync date from freshly
/// fetched prices (<see cref="UpsertPortfolio"/>), and the position-event consumers, which revalue
/// one portfolio for today from the locally stored last prices and rates (<see cref="RevalueTodayAsync"/>).
/// </summary>
/// <remarks>
/// Not a service layer: no state, no interface, a small helper next to <see cref="ValuationAlgorithm"/>
/// so the upsert logic exists once. Every write goes through the change tracker and the caller's
/// DbContext, so it commits in the same transaction as the consumer's inbox row (ADR-012).
/// </remarks>
public sealed class PortfolioSnapshotWriter(ReportingDbContext db, TimeProvider timeProvider)
{
    private const string BaseCurrency = "PLN"; // ADR-008

    /// <summary>Today as the event path values it: the UTC date, matching <c>DailyPricesSynced.SyncDate</c> (spec-07 design decision 2).</summary>
    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Every non-PLN currency→PLN pair a valuation of <paramref name="positions"/> needs: a market
    /// asset's own quote currency (not its <c>Currency</c> — the price is denominated in whatever the
    /// instrument quotes in), or a manual/currency-valued asset's own <c>Currency</c> (both
    /// non-market modes key off it, so filtering on <c>InstrumentId is null</c> covers them).
    /// </summary>
    public static IReadOnlyList<string> RequiredFxPairs(
        IEnumerable<Position> positions, IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument) =>
        pricesByInstrument.Values.Select(p => p.QuoteCurrency)
            .Concat(positions.Where(p => p.InstrumentId is null).Select(p => p.Currency))
            .Where(c => !string.Equals(c, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToUpperInvariant() + BaseCurrency)
            .Distinct()
            .ToList();

    /// <summary>
    /// Event path (spec-07): values <paramref name="portfolioId"/>'s non-archived positions for
    /// today from <see cref="LatestInstrumentPrice"/>/<see cref="LatestFxRate"/> only — never REST —
    /// then upserts today's lines and snapshot under <paramref name="userId"/> (the event's) and saves.
    /// A portfolio with no positions left gets a zero snapshot, and today's line of any asset no
    /// longer held is removed, so lines and snapshot always agree. Earlier dates are never touched.
    /// </summary>
    public async Task RevalueTodayAsync(Guid portfolioId, Guid userId, CancellationToken cancellationToken)
    {
        var today = Today;

        // spec-07 design decision 5: serialize revaluations of one portfolio across consumers
        // (AssetPositionChanged and AssetRemoved have separate queues), so none writes a snapshot
        // from a read another is about to invalidate. Transaction-scoped: released when the inbox
        // transaction this runs in commits or rolls back.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey(portfolioId)})", cancellationToken);

        // IgnoreQueryFilters on every read below: a consumer has no request user (ICurrentUser is
        // Guid.Empty) and writes on behalf of the user the event names — see
        // AssetPositionChangedConsumer for the full rationale.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId && !p.PortfolioIsArchived)
            .ToListAsync(cancellationToken);

        var pricesByInstrument = await LoadLatestPricesAsync(positions, cancellationToken);
        var fxRatesByPair = await LoadLatestFxRatesAsync(RequiredFxPairs(positions, pricesByInstrument), cancellationToken);

        var existingSnapshot = await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Date == today, cancellationToken);

        var existingLines = await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId && l.Date == today)
            .ToDictionaryAsync(l => l.AssetId, cancellationToken);

        UpsertPortfolio(portfolioId, userId, positions, pricesByInstrument, fxRatesByPair, today, existingSnapshot, existingLines);

        var heldAssetIds = positions.Select(p => p.AssetId).ToHashSet();
        db.AssetValuations.RemoveRange(existingLines.Values.Where(l => !heldAssetIds.Contains(l.AssetId)));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Values <paramref name="positions"/> for <paramref name="snapshotDate"/> and stages the lines and
    /// snapshot in the change tracker — no I/O, so the sync consumer's per-portfolio isolation holds:
    /// nothing reaches Postgres for a portfolio until the caller saves.
    /// </summary>
    public void UpsertPortfolio(
        Guid portfolioId,
        Guid userId,
        IReadOnlyList<Position> positions,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate,
        ValuationSnapshot? existingSnapshot,
        IReadOnlyDictionary<Guid, AssetValuation> existingLines)
    {
        var valuationPositions = positions
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

        if (existingSnapshot is not null)
        {
            existingSnapshot.UserId = userId;
            existingSnapshot.TotalPln = result.TotalPln;
            existingSnapshot.IsStale = result.IsStale;
            return;
        }

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

    private async Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> LoadLatestPricesAsync(
        IReadOnlyList<Position> positions, CancellationToken cancellationToken)
    {
        var instrumentIds = positions
            .Where(p => p.InstrumentId is { })
            .Select(p => p.InstrumentId!.Value)
            .Distinct()
            .ToList();

        if (instrumentIds.Count == 0)
        {
            return new Dictionary<Guid, InstrumentPriceLookup>();
        }

        return await db.LatestInstrumentPrices
            .AsNoTracking()
            .Where(p => instrumentIds.Contains(p.InstrumentId))
            .ToDictionaryAsync(
                p => p.InstrumentId,
                p => new InstrumentPriceLookup(p.QuoteCurrency, p.Date, p.Close),
                cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, FxRateLookup>> LoadLatestFxRatesAsync(
        IReadOnlyList<string> pairs, CancellationToken cancellationToken)
    {
        if (pairs.Count == 0)
        {
            return new Dictionary<string, FxRateLookup>(StringComparer.OrdinalIgnoreCase);
        }

        return await db.LatestFxRates
            .AsNoTracking()
            .Where(r => pairs.Contains(r.Pair))
            .ToDictionaryAsync(
                r => r.Pair,
                r => new FxRateLookup(r.Date, r.Rate),
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
    }

    /// <summary>A portfolio id folded into Postgres' 64-bit advisory lock key space. A collision only over-serializes two portfolios, it never under-serializes one.</summary>
    private static long AdvisoryLockKey(Guid portfolioId) => BitConverter.ToInt64(portfolioId.ToByteArray(), 0);
}
