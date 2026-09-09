using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Valuation;

/// <summary>
/// Pure implementation of 03-domain-model.md §Valuation algorithm — no DB/HTTP access, so it's
/// unit-testable directly (T2.11 AC). The consumer resolves positions/prices/FX over REST first,
/// then calls <see cref="Calculate"/> per portfolio.
/// </summary>
public static class ValuationAlgorithm
{
    private const string BaseCurrency = "PLN"; // ADR-008
    private const int StaleThresholdDays = 7;

    public static ValuationResult Calculate(
        IReadOnlyList<ValuationPosition> positions,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate)
    {
        var isStale = false;
        var valuedPositions = new List<(AssetClass AssetClass, decimal ValuePln)>();

        foreach (var position in positions)
        {
            var (valuePln, positionIsStale) = position.ValuationMode switch
            {
                AssetValuationMode.Market => ValueMarketAsset(position, pricesByInstrument, fxRatesByPair, snapshotDate),
                AssetValuationMode.CurrencyValued => ValueCurrencyValuedAsset(position, fxRatesByPair, snapshotDate),
                _ => ValueManualAsset(position, fxRatesByPair, snapshotDate),
            };

            isStale |= positionIsStale;

            if (valuePln is { } value)
            {
                valuedPositions.Add((position.AssetClass, value));
            }
        }

        var breakdown = valuedPositions
            .GroupBy(p => p.AssetClass)
            .Select(g => new AssetClassBreakdownEntry(g.Key, g.Sum(p => p.ValuePln)))
            .OrderByDescending(e => e.ValuePln)
            .ToList();

        return new ValuationResult
        {
            TotalPln = valuedPositions.Sum(p => p.ValuePln),
            Breakdown = breakdown,
            IsStale = isStale,
        };
    }

    private static (decimal? ValuePln, bool IsStale) ValueMarketAsset(
        ValuationPosition position,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate)
    {
        // Defensive, not expected: Portfolio is the sole writer of ValuationMode and InstrumentId
        // together, so Market without an InstrumentId shouldn't happen — stale-and-unvalued beats
        // throwing if it ever does.
        if (position.InstrumentId is not { } instrumentId || !pricesByInstrument.TryGetValue(instrumentId, out var price))
        {
            // No quote at all (not even an old one) — nothing to value, but flag it.
            return (null, true);
        }

        var (rate, fxIsStale) = ResolveFxRate(price.QuoteCurrency, fxRatesByPair, snapshotDate);
        if (rate is null)
        {
            return (null, true);
        }

        var priceIsStale = IsOlderThanThreshold(price.Date, snapshotDate);
        return (position.Quantity * price.Close * rate.Value, priceIsStale || fxIsStale);
    }

    private static (decimal? ValuePln, bool IsStale) ValueManualAsset(
        ValuationPosition position,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate)
    {
        var (rate, fxIsStale) = ResolveFxRate(position.Currency, fxRatesByPair, snapshotDate);
        if (rate is null)
        {
            return (null, true);
        }

        return ((position.ManualValueAmount ?? 0m) * rate.Value, fxIsStale);
    }

    /// <summary>M1.4's third mode: no instrument, no manual amount — <see cref="ValuationPosition.Quantity"/>
    /// itself is the amount held in <see cref="ValuationPosition.Currency"/> (e.g. plain cash, a term
    /// deposit), converted through the same <see cref="ResolveFxRate"/> the other two modes use.</summary>
    private static (decimal? ValuePln, bool IsStale) ValueCurrencyValuedAsset(
        ValuationPosition position,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate)
    {
        var (rate, fxIsStale) = ResolveFxRate(position.Currency, fxRatesByPair, snapshotDate);
        if (rate is null)
        {
            return (null, true);
        }

        return (position.Quantity * rate.Value, fxIsStale);
    }

    private static (decimal? Rate, bool IsStale) ResolveFxRate(
        string currency, IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair, DateOnly snapshotDate)
    {
        if (string.Equals(currency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return (1m, false);
        }

        var pair = currency.ToUpperInvariant() + BaseCurrency;
        if (!fxRatesByPair.TryGetValue(pair, out var rate))
        {
            return (null, true);
        }

        return (rate.Rate, IsOlderThanThreshold(rate.Date, snapshotDate));
    }

    private static bool IsOlderThanThreshold(DateOnly date, DateOnly snapshotDate) =>
        snapshotDate.DayNumber - date.DayNumber > StaleThresholdDays;
}
