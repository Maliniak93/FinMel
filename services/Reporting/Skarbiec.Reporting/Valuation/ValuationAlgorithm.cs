using Skarbiec.Contracts;
using Skarbiec.Contracts.Bonds;
using Skarbiec.Contracts.Events;

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
        DateOnly snapshotDate,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, decimal>>? bondRatesBySeries = null)
    {
        var lines = new List<ValuedPosition>(positions.Count);

        foreach (var position in positions)
        {
            lines.Add(position.ValuationMode switch
            {
                AssetValuationMode.Market => ValueMarketAsset(position, pricesByInstrument, fxRatesByPair, snapshotDate),
                AssetValuationMode.CurrencyValued when position.BondTerms is { } terms =>
                    ValueBond(position, terms, bondRatesBySeries, snapshotDate),
                AssetValuationMode.CurrencyValued => ValueCurrencyValuedAsset(position, fxRatesByPair, snapshotDate),
                _ => throw new ArgumentOutOfRangeException(nameof(positions), position.ValuationMode, "Unmapped AssetValuationMode."),
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

    // Bonds are PLN. A missing catalogue rate keeps the book value and marks the line stale, never zero.
    private static ValuedPosition ValueBond(
        ValuationPosition position,
        PositionBondTerms terms,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, decimal>>? bondRatesBySeries,
        DateOnly snapshotDate)
    {
        if (terms.IsRedeemed)
        {
            return Line(position, position.Quantity, isStale: false, fxRateUsed: 1m);
        }

        var bondCount = terms.BondCountHistory
            .Where(e => e.Key <= snapshotDate)
            .OrderBy(e => e.Key)
            .Select(e => e.Value)
            .LastOrDefault();
        IReadOnlyDictionary<int, decimal> catalogRates = bondRatesBySeries is not null
            && bondRatesBySeries.TryGetValue(terms.SeriesCode, out var rates)
                ? rates
                : new Dictionary<int, decimal>();

        var interest = BondAccrual.Unsettled(
            new BondTerms(terms.Type, terms.PurchaseDate, bondCount, terms.FirstPeriodRatePercent),
            terms.SettledPeriodRates,
            catalogRates,
            snapshotDate);

        return interest is null
            ? Line(position, position.Quantity, isStale: true, fxRateUsed: 1m)
            : Line(position, position.Quantity + interest.Value, isStale: false, fxRateUsed: 1m);
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
            ValuePln = Math.Round(valuePln, 2, MidpointRounding.AwayFromZero),
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
