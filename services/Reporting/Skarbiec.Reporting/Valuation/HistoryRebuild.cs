using Skarbiec.Contracts;
using Skarbiec.Contracts.Bonds;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

// Pure, with no DB or HTTP access: values one user's positions for every day of a range through ValuationAlgorithm, from quantity history and price/FX series.
public static class HistoryRebuild
{
    // Series are ascending by date, so one forward-only cursor per series finds "latest on or before" without searching.
    public static IReadOnlyList<RebuiltDay> Compute(
        IReadOnlyList<Position> positions,
        IReadOnlyDictionary<Guid, IReadOnlyList<InstrumentPriceLookup>> priceSeries,
        IReadOnlyDictionary<string, IReadOnlyList<FxRateLookup>> fxSeries,
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, decimal>>? bondRatesBySeries = null)
    {
        var states = positions.Select(p => new PositionState(p)).ToList();
        var priceCursors = priceSeries.ToDictionary(s => s.Key, s => new Cursor<InstrumentPriceLookup>(s.Value, p => p.Date));
        var fxCursors = fxSeries.ToDictionary(s => s.Key, s => new Cursor<FxRateLookup>(s.Value, r => r.Date), StringComparer.OrdinalIgnoreCase);

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

            var valued = new List<ValuationPosition>();
            foreach (var state in states)
            {
                var quantity = state.QuantityOn(day);
                if (state.IsArchivedOn(day))
                {
                    continue;
                }

                if (state.HasLineOn(day))
                {
                    valued.Add(state.ToValuationPosition(day, quantity));
                }
            }

            var lines = ValuationAlgorithm.Calculate(valued, prices, fxRates, day, bondRatesBySeries).Lines;

            if (lines.Count == 0)
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

    private sealed class PositionState(Position position)
    {
        private int _next;

        public bool IsArchivedOn(DateOnly day) => position.ArchivedOn is { } archivedOn && day >= archivedOn;

        public bool HasLineOn(DateOnly day) => position.QuantityHistory.Count > 0 && position.QuantityHistory[0].Date <= day;

        // Days must be asked in ascending order.
        public decimal QuantityOn(DateOnly day)
        {
            var history = position.QuantityHistory;
            while (_next < history.Count && history[_next].Date <= day)
            {
                _next++;
            }

            return _next > 0 ? history[_next - 1].Quantity : 0m;
        }

        public ValuationPosition ToValuationPosition(DateOnly day, decimal quantity) => new()
        {
            AssetId = position.AssetId,
            AssetClass = position.ClassOn(day),
            ValuationMode = position.ValuationMode,
            Currency = position.Currency,
            Quantity = quantity,
            QuoteUnitsPerQuantity = position.QuoteUnitsPerQuantity,
            InstrumentId = position.InstrumentId,
            BondTerms = BondTermsOn(day),
        };

        // A period counts as settled on a day only once it has ended: its credit is dated the period end and is already in the quantity from then on.
        private PositionBondTerms? BondTermsOn(DateOnly day)
        {
            if (position.BondTerms is not { } terms)
            {
                return null;
            }

            _periodEnds ??= BondSchedule.Periods(terms.Type, terms.PurchaseDate).ToDictionary(p => p.Index, p => p.End);
            var settled = terms.SettledPeriodRates
                .Where(e => _periodEnds.TryGetValue(e.Key, out var end) && end <= day)
                .ToDictionary(e => e.Key, e => e.Value);

            return terms with { SettledPeriodRates = settled, IsRedeemed = false };
        }

        private Dictionary<int, DateOnly>? _periodEnds;
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
