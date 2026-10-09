using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

// Pure, with no DB or HTTP access: values one portfolio for every day of a range through ValuationAlgorithm, from quantity history and price/FX series.
public static class HistoryRebuild
{
    // Series are ascending by date, so one forward-only cursor per series finds "latest on or before" without searching.
    public static IReadOnlyList<RebuiltDay> Compute(
        IReadOnlyList<Position> positions,
        IReadOnlyDictionary<Guid, IReadOnlyList<InstrumentPriceLookup>> priceSeries,
        IReadOnlyDictionary<string, IReadOnlyList<FxRateLookup>> fxSeries,
        DateOnly from,
        DateOnly to,
        IEnumerable<AssetValuation>? existingLines = null)
    {
        var portfolioArchivedOn = positions.Min(p => p.PortfolioArchivedOn);
        var states = positions.Select(p => new PositionState(p)).ToList();
        var priceCursors = priceSeries.ToDictionary(s => s.Key, s => new Cursor<InstrumentPriceLookup>(s.Value, p => p.Date));
        var fxCursors = fxSeries.ToDictionary(s => s.Key, s => new Cursor<FxRateLookup>(s.Value, r => r.Date), StringComparer.OrdinalIgnoreCase);
        var storedLines = StoredManualLines(positions, existingLines);

        var days = new List<RebuiltDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var prices = new Dictionary<Guid, InstrumentPriceLookup>();
            foreach (var (instrumentId, cursor) in priceCursors)
            {
                if (cursor.TryAdvance(day, out var price))
                {
                    prices[instrumentId] = price;
                }
            }

            var fxRates = new Dictionary<string, FxRateLookup>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pair, cursor) in fxCursors)
            {
                if (cursor.TryAdvance(day, out var rate))
                {
                    fxRates[pair] = rate;
                }
            }

            var archivedPortfolio = portfolioArchivedOn is { } archivedOn && day >= archivedOn;
            var valued = new List<ValuationPosition>();
            var keptLines = new List<ValuedPosition>();
            foreach (var state in states)
            {
                var quantity = state.QuantityOn(day);
                if (archivedPortfolio || state.IsArchivedOn(day))
                {
                    continue;
                }

                if (state.HasLineOn(day))
                {
                    valued.Add(state.ToValuationPosition(quantity));
                }
                else if (storedLines.TryGetValue((state.Position.AssetId, day), out var stored))
                {
                    keptLines.Add(ToValuedPosition(stored));
                }
            }

            var result = ValuationAlgorithm.Calculate(valued, prices, fxRates, day);
            var lines = result.Lines.Concat(keptLines).ToList();

            if (lines.Count == 0 && !archivedPortfolio)
            {
                continue;
            }

            days.Add(new RebuiltDay
            {
                Date = day,
                Lines = lines,
                TotalPln = lines.Sum(l => l.ValuePln),
                IsStale = lines.Any(l => l.IsStale),
            });
        }

        return days;
    }

    // Only a Manual position's line dated before its ManualValueDate is kept: it is the sole record of an earlier manual value.
    private static Dictionary<(Guid AssetId, DateOnly Date), AssetValuation> StoredManualLines(
        IReadOnlyList<Position> positions, IEnumerable<AssetValuation>? existingLines)
    {
        var result = new Dictionary<(Guid, DateOnly), AssetValuation>();
        if (existingLines is null)
        {
            return result;
        }

        var manualValueDates = positions
            .Where(p => p.ValuationMode == AssetValuationMode.Manual)
            .ToDictionary(p => p.AssetId, p => p.ManualValueDate);

        foreach (var line in existingLines)
        {
            if (manualValueDates.TryGetValue(line.AssetId, out var manualValueDate) && line.Date < manualValueDate)
            {
                result[(line.AssetId, line.Date)] = line;
            }
        }

        return result;
    }

    private static ValuedPosition ToValuedPosition(AssetValuation line) => new()
    {
        AssetId = line.AssetId,
        AssetClass = line.AssetClass,
        Quantity = line.Quantity,
        PriceUsed = line.PriceUsed,
        PriceDate = line.PriceDate,
        FxRateUsed = line.FxRateUsed,
        ValuePln = line.ValuePln,
        IsStale = line.IsStale,
    };

    private sealed class PositionState(Position position)
    {
        private int _next;

        public Position Position => position;

        public bool IsArchivedOn(DateOnly day) => position.ArchivedOn is { } archivedOn && day >= archivedOn;

        // Market and currency-valued: from the first transaction. Manual: from the value's own date.
        public bool HasLineOn(DateOnly day) => position.ValuationMode == AssetValuationMode.Manual
            ? position.ManualValueDate is { } manualValueDate && manualValueDate <= day
            : position.QuantityHistory.Count > 0 && position.QuantityHistory[0].Date <= day;

        // Days must be asked in ascending order. A Manual position without history keeps its current quantity.
        public decimal QuantityOn(DateOnly day)
        {
            var history = position.QuantityHistory;
            while (_next < history.Count && history[_next].Date <= day)
            {
                _next++;
            }

            if (_next > 0)
            {
                return history[_next - 1].Quantity;
            }

            return position.ValuationMode == AssetValuationMode.Manual ? position.Quantity : 0m;
        }

        public ValuationPosition ToValuationPosition(decimal quantity) => new()
        {
            AssetId = position.AssetId,
            AssetClass = position.AssetClass,
            ValuationMode = position.ValuationMode,
            Currency = position.Currency,
            Quantity = quantity,
            QuoteUnitsPerQuantity = position.QuoteUnitsPerQuantity,
            InstrumentId = position.InstrumentId,
            ManualValueAmount = position.ManualValueAmount,
        };
    }

    private sealed class Cursor<T>(IReadOnlyList<T> series, Func<T, DateOnly> date)
    {
        private int _next;

        public bool TryAdvance(DateOnly day, out T value)
        {
            while (_next < series.Count && date(series[_next]) <= day)
            {
                _next++;
            }

            value = _next > 0 ? series[_next - 1] : default!;
            return _next > 0;
        }
    }
}
