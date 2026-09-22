using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Valuation;

/// <summary>
/// Pure implementation of 03-domain-model.md §Valuation algorithm — no DB/HTTP access, so it's
/// unit-testable directly (spec-03 design decision 6). The consumer reads positions from its own
/// <c>Position</c> table and prices/FX from MarketData first, then calls <see cref="Calculate"/> per
/// portfolio.
/// </summary>
/// <remarks>
/// Contract: exactly one <see cref="ValuedPosition"/> per input position, always. A missing quote or
/// rate produces a zero-valued, stale line instead of dropping the position — the consumer writes
/// one <c>AssetValuation</c> row per position and relies on that (spec-03 AC10).
/// </remarks>
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
        var lines = new List<ValuedPosition>(positions.Count);

        foreach (var position in positions)
        {
            lines.Add(position.ValuationMode switch
            {
                AssetValuationMode.Market => ValueMarketAsset(position, pricesByInstrument, fxRatesByPair, snapshotDate),
                AssetValuationMode.CurrencyValued => ValueCurrencyValuedAsset(position, fxRatesByPair, snapshotDate),
                _ => ValueManualAsset(position, fxRatesByPair, snapshotDate),
            });
        }

        return new ValuationResult
        {
            Lines = lines,
            TotalPln = lines.Sum(l => l.ValuePln),
            IsStale = lines.Any(l => l.IsStale),
        };
    }

    private static ValuedPosition ValueMarketAsset(
        ValuationPosition position,
        IReadOnlyDictionary<Guid, InstrumentPriceLookup> pricesByInstrument,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate)
    {
        // Defensive, not expected: Portfolio is the sole writer of ValuationMode and InstrumentId
        // together, so Market without an InstrumentId shouldn't happen — a zero-valued stale line
        // beats throwing if it ever does.
        if (position.InstrumentId is not { } instrumentId || !pricesByInstrument.TryGetValue(instrumentId, out var price))
        {
            // No quote at all (not even an old one) — nothing to value, but the line still exists.
            return Line(position, valuePln: 0m, isStale: true);
        }

        var (rate, fxIsStale) = ResolveFxRate(price.QuoteCurrency, fxRatesByPair, snapshotDate);
        if (rate is null)
        {
            return Line(position, valuePln: 0m, isStale: true, priceUsed: price.Close, priceDate: price.Date);
        }

        var priceIsStale = IsOlderThanThreshold(price.Date, snapshotDate);
        return Line(
            position,
            valuePln: position.Quantity * price.Close * rate.Value,
            isStale: priceIsStale || fxIsStale,
            priceUsed: price.Close,
            priceDate: price.Date,
            fxRateUsed: rate);
    }

    private static ValuedPosition ValueManualAsset(
        ValuationPosition position,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate)
    {
        var (rate, fxIsStale) = ResolveFxRate(position.Currency, fxRatesByPair, snapshotDate);

        return rate is null
            ? Line(position, valuePln: 0m, isStale: true)
            : Line(position, (position.ManualValueAmount ?? 0m) * rate.Value, fxIsStale, fxRateUsed: rate);
    }

    /// <summary>M1.4's third mode: no instrument, no manual amount — <see cref="ValuationPosition.Quantity"/>
    /// itself is the amount held in <see cref="ValuationPosition.Currency"/> (e.g. plain cash, a term
    /// deposit), converted through the same <see cref="ResolveFxRate"/> the other two modes use.</summary>
    private static ValuedPosition ValueCurrencyValuedAsset(
        ValuationPosition position,
        IReadOnlyDictionary<string, FxRateLookup> fxRatesByPair,
        DateOnly snapshotDate)
    {
        var (rate, fxIsStale) = ResolveFxRate(position.Currency, fxRatesByPair, snapshotDate);

        return rate is null
            ? Line(position, valuePln: 0m, isStale: true)
            : Line(position, position.Quantity * rate.Value, fxIsStale, fxRateUsed: rate);
    }

    private static ValuedPosition Line(
        ValuationPosition position,
        decimal valuePln,
        bool isStale,
        decimal? priceUsed = null,
        DateOnly? priceDate = null,
        decimal? fxRateUsed = null) => new()
        {
            AssetId = position.AssetId,
            AssetClass = position.AssetClass,
            Quantity = position.Quantity,
            PriceUsed = priceUsed,
            PriceDate = priceDate,
            FxRateUsed = fxRateUsed,
            ValuePln = valuePln,
            IsStale = isStale,
        };

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
