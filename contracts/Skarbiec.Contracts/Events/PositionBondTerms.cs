namespace Skarbiec.Contracts.Events;

public sealed record PositionBondTerms
{
    public required string SeriesCode { get; init; }
    public required TreasuryBondType Type { get; init; }
    public required DateOnly PurchaseDate { get; init; }
    public required decimal PurchasePricePerBond { get; init; }
    public required decimal FirstPeriodRatePercent { get; init; }
    public decimal? MarginPercent { get; init; }

    /// <summary>Bonds held from each date on, ascending by date.</summary>
    public required IReadOnlyDictionary<DateOnly, int> BondCountHistory { get; init; }

    /// <summary>Rate in percent of each interest period a settlement has credited, keyed by period index.</summary>
    public required IReadOnlyDictionary<int, decimal> SettledPeriodRates { get; init; }

    public required bool IsRedeemed { get; init; }

    public bool SameAs(PositionBondTerms? other) =>
        other is not null
        && SeriesCode == other.SeriesCode
        && Type == other.Type
        && PurchaseDate == other.PurchaseDate
        && PurchasePricePerBond == other.PurchasePricePerBond
        && FirstPeriodRatePercent == other.FirstPeriodRatePercent
        && MarginPercent == other.MarginPercent
        && IsRedeemed == other.IsRedeemed
        && SameEntries(BondCountHistory, other.BondCountHistory)
        && SameEntries(SettledPeriodRates, other.SettledPeriodRates);

    private static bool SameEntries<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> a, IReadOnlyDictionary<TKey, TValue> b)
        where TKey : notnull
        where TValue : IEquatable<TValue> =>
        a.Count == b.Count && a.All(e => b.TryGetValue(e.Key, out var value) && e.Value.Equals(value));
}
