using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

// Pure: the earliest day whose valuation an incoming event changes, as computed - the caller decides whether it lies in the past.
public static class HistoryChange
{
    public static DateOnly? EarliestAffectedDate(Position? stored, AssetPositionChanged incoming)
    {
        var newStart = Start(incoming.ValuationMode, incoming.QuantityHistory.Select(p => p.Date), incoming.ManualValueDate);
        if (stored is null)
        {
            return newStart;
        }

        var oldStart = Start(stored.ValuationMode, stored.QuantityHistory.Select(p => p.Date), stored.ManualValueDate);
        var candidates = new List<DateOnly?>();

        if (stored.AssetClass != incoming.AssetClass
            || stored.ValuationMode != incoming.ValuationMode
            || stored.InstrumentId != incoming.InstrumentId
            || !string.Equals(stored.Currency, incoming.Currency, StringComparison.Ordinal))
        {
            candidates.Add(Earlier(oldStart, newStart));
        }

        candidates.Add(FirstQuantityDifference(stored.QuantityHistory, incoming.QuantityHistory));

        if (stored.ManualValueAmount != incoming.ManualValueAmount || stored.ManualValueDate != incoming.ManualValueDate)
        {
            candidates.Add(Earlier(stored.ManualValueDate, incoming.ManualValueDate));
        }

        if (stored.IsArchived != incoming.IsArchived)
        {
            candidates.Add(incoming.IsArchived ? UtcDate(incoming.OccurredAtUtc) : stored.ArchivedOn);
        }

        if (stored.PortfolioIsArchived != incoming.PortfolioIsArchived)
        {
            candidates.Add(incoming.PortfolioIsArchived ? UtcDate(incoming.OccurredAtUtc) : stored.PortfolioArchivedOn);
        }

        return candidates.Aggregate((DateOnly?)null, Earlier);
    }

    public static DateOnly UtcDate(DateTimeOffset occurredAtUtc) => DateOnly.FromDateTime(occurredAtUtc.UtcDateTime);

    private static DateOnly? Start(AssetValuationMode mode, IEnumerable<DateOnly> historyDates, DateOnly? manualValueDate) =>
        mode == AssetValuationMode.Manual
            ? manualValueDate
            : historyDates.Select(d => (DateOnly?)d).Min();

    // A series is 0 before its first point, so the first difference is always at a point of one of the two.
    private static DateOnly? FirstQuantityDifference(
        IReadOnlyList<PositionQuantityPoint> oldHistory, IReadOnlyList<QuantityPoint> newHistory)
    {
        var dates = oldHistory.Select(p => p.Date).Concat(newHistory.Select(p => p.Date)).Distinct().Order();

        foreach (var date in dates)
        {
            var oldQuantity = oldHistory.LastOrDefault(p => p.Date <= date)?.Quantity ?? 0m;
            var newQuantity = newHistory.LastOrDefault(p => p.Date <= date)?.Quantity ?? 0m;
            if (oldQuantity != newQuantity)
            {
                return date;
            }
        }

        return null;
    }

    private static DateOnly? Earlier(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;
}
