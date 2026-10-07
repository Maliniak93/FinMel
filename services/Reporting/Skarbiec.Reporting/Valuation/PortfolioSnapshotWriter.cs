using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

// Not a service layer: a stateless helper so the upsert logic exists once, writing through the caller's DbContext.
public sealed class PortfolioSnapshotWriter(ReportingDbContext db, TimeProvider timeProvider)
{
    private const string BaseCurrency = "PLN";

    // The UTC date, matching DailyPricesSynced.SyncDate.
    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    // A market asset converts from its quote currency, not its Currency; the other modes key off Currency.
    public static IReadOnlyList<string> RequiredFxPairs(
        IEnumerable<Position> positions, IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument) =>
        pricesByInstrument.Values.Select(p => p.QuoteCurrency)
            .Concat(positions.Where(p => p.InstrumentId is null).Select(p => p.Currency))
            .Where(c => !string.Equals(c, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToUpperInvariant() + BaseCurrency)
            .Distinct()
            .ToList();

    // Values from the stored last prices and rates only, and drops today's lines of assets that are gone.
    public async Task RevalueTodayAsync(Guid portfolioId, Guid userId, CancellationToken cancellationToken)
    {
        var today = Today;

        // Serializes revaluations of one portfolio across queues; released when the inbox transaction ends.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey(portfolioId)})", cancellationToken);

        // IgnoreQueryFilters: a consumer has no request user and writes for the user the event names.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.PortfolioId == portfolioId && !p.PortfolioIsArchived && !p.IsArchived)
            .ToListAsync(cancellationToken);

        var pricesByInstrument = await LoadLatestPricesAsync(positions, cancellationToken);
        var fxRatesByPair = await LoadLatestFxRatesAsync(RequiredFxPairs(positions, pricesByInstrument), cancellationToken);

        // IgnoreQueryFilters: the snapshot belongs to the event's user, not to a request user.
        var existingSnapshot = await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Date == today, cancellationToken);

        // IgnoreQueryFilters: the lines belong to the event's user, not to a request user.
        var existingLines = await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.PortfolioId == portfolioId && l.Date == today)
            .ToDictionaryAsync(l => l.AssetId, cancellationToken);

        UpsertPortfolio(portfolioId, userId, positions, pricesByInstrument, fxRatesByPair, today, existingSnapshot, existingLines);

        var heldAssetIds = positions.Select(p => p.AssetId).ToHashSet();
        db.AssetValuations.RemoveRange(existingLines.Values.Where(l => !heldAssetIds.Contains(l.AssetId)));

        await db.SaveChangesAsync(cancellationToken);
    }

    // Stages only, with no I/O, so nothing reaches Postgres for a portfolio until the caller saves.
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
                QuoteUnitsPerQuantity = p.QuoteUnitsPerQuantity,
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

    // A collision only over-serializes two portfolios; it never under-serializes one.
    private static long AdvisoryLockKey(Guid portfolioId) => BitConverter.ToInt64(portfolioId.ToByteArray(), 0);
}
