using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

// Not a service layer: a stateless helper so the upsert logic exists once, writing through the caller's DbContext.
public sealed class SnapshotWriter(ReportingDbContext db, TimeProvider timeProvider)
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
    public async Task RevalueTodayAsync(ConsumeContext context, Guid userId, CancellationToken cancellationToken)
    {
        var today = Today;

        await LockUserAsync(db, userId, cancellationToken);

        // IgnoreQueryFilters: a consumer has no request user and writes for the user the event names.
        var positions = await db.Positions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.UserId == userId && !p.IsArchived)
            .ToListAsync(cancellationToken);

        var pricesByInstrument = await LoadLatestPricesAsync(positions, cancellationToken);
        var fxRatesByPair = await LoadLatestFxRatesAsync(RequiredFxPairs(positions, pricesByInstrument), cancellationToken);

        // IgnoreQueryFilters: the snapshot belongs to the event's user, not to a request user.
        var existingSnapshot = await db.ValuationSnapshots
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.UserId == userId && s.Date == today, cancellationToken);

        // IgnoreQueryFilters: the lines belong to the event's user, not to a request user.
        var existingLines = await db.AssetValuations
            .IgnoreQueryFilters()
            .Where(l => l.UserId == userId && l.Date == today)
            .ToDictionaryAsync(l => l.AssetId, cancellationToken);

        UpsertUser(userId, positions, pricesByInstrument, fxRatesByPair, today, existingSnapshot, existingLines);

        var heldAssetIds = positions.Select(p => p.AssetId).ToHashSet();
        db.AssetValuations.RemoveRange(existingLines.Values.Where(l => !heldAssetIds.Contains(l.AssetId)));

        await db.SaveChangesAsync(cancellationToken);

        await LatestValuation.PublishAsync(db, context, timeProvider, userId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Serializes writers of one user across queues; released when the inbox transaction ends.
    public static Task LockUserAsync(ReportingDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey(userId)})", cancellationToken);

    // Stages only, with no I/O, so nothing reaches Postgres for a user until the caller saves.
    public void UpsertUser(
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
            })
            .ToList();

        var result = ValuationAlgorithm.Calculate(valuationPositions, pricesByInstrument, fxRatesByPair, snapshotDate);

        var accountByAsset = positions.ToDictionary(p => p.AssetId, p => p.AccountId);

        foreach (var valued in result.Lines)
        {
            UpsertLine(accountByAsset[valued.AssetId], userId, valued, snapshotDate, existingLines);
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
            Date = snapshotDate,
            TotalPln = result.TotalPln,
            IsStale = result.IsStale,
        });
    }

    private void UpsertLine(
        Guid? accountId,
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
            AccountId = accountId,
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

    // A collision only over-serializes two users; it never under-serializes one.
    private static long AdvisoryLockKey(Guid userId) => BitConverter.ToInt64(userId.ToByteArray(), 0);
}
