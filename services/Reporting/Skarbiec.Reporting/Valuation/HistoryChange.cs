using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

// Pure: the earliest day whose valuation an incoming event changes, as computed - the caller decides whether it lies in the past.
public static class HistoryChange
{
    public static DateOnly? EarliestAffectedDate(Position? stored, AssetPositionChanged incoming)
    {
        var newStart = Start(incoming.QuantityHistory.Select(p => p.Date));
        if (stored is null)
        {
            return newStart;
        }

        var oldStart = Start(stored.QuantityHistory.Select(p => p.Date));
        var candidates = new List<DateOnly?>();

        if (stored.ValuationMode != incoming.ValuationMode
            || stored.InstrumentId != incoming.InstrumentId
            || !string.Equals(stored.Currency, incoming.Currency, StringComparison.Ordinal))
        {
            candidates.Add(Earlier(oldStart, newStart));
        }

        candidates.Add(FirstClassDifference(stored, incoming, oldStart, newStart));
        candidates.Add(FirstQuantityDifference(stored.QuantityHistory, incoming.QuantityHistory));

        if (stored.IsArchived != incoming.IsArchived)
        {
            candidates.Add(incoming.IsArchived ? UtcDate(incoming.OccurredAtUtc) : stored.ArchivedOn);
        }

        return candidates.Aggregate((DateOnly?)null, Earlier);
    }

    public static DateOnly UtcDate(DateTimeOffset occurredAtUtc) => DateOnly.FromDateTime(occurredAtUtc.UtcDateTime);

    private static DateOnly? Start(IEnumerable<DateOnly> historyDates) => historyDates.Select(d => (DateOnly?)d).Min();

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

    // The class is constant between points, so the first difference is at a point or at a quantity start of one of the two.
    private static DateOnly? FirstClassDifference(
        Position stored, AssetPositionChanged incoming, DateOnly? oldStart, DateOnly? newStart)
    {
        var dates = stored.ClassHistory.Select(p => p.Date)
            .Concat(incoming.ClassHistory.Select(p => p.Date))
            .Concat(new[] { oldStart, newStart }.OfType<DateOnly>())
            .Distinct()
            .Order();

        foreach (var date in dates)
        {
            var oldClass = stored.ClassOn(date);
            var newClass = incoming.ClassHistory.LastOrDefault(p => p.Date <= date)?.AssetClass ?? incoming.AssetClass;
            if (oldClass != newClass)
            {
                return date;
            }
        }

        return null;
    }

    private static DateOnly? Earlier(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;
}
