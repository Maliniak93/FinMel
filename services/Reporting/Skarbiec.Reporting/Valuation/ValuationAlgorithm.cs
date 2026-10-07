using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Valuation;

// Pure, with no DB or HTTP access: exactly one line per input position, and a missing quote or rate gives a zero-valued stale line.
public static class ValuationAlgorithm
{
    private const string BaseCurrency = "PLN";
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
        // Defensive: Portfolio always sets InstrumentId with Market, and a stale zero line beats throwing.
        if (position.InstrumentId is not { } instrumentId || !pricesByInstrument.TryGetValue(instrumentId, out var price))
        {
            // No quote at all, not even an old one: nothing to value, but the line still exists.
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
            valuePln: position.Quantity * position.QuoteUnitsPerQuantity * price.Close * rate.Value,
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

    // Currency-valued: Quantity is the amount held in Currency, converted like the other modes.
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
