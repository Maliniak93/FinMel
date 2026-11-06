namespace Skarbiec.Contracts.Bonds;

public sealed record BondTerms(TreasuryBondType Type, DateOnly PurchaseDate, int BondCount, decimal FirstPeriodRatePercent);

public static class BondAccrual
{
    // PLN gross for the whole holding that no settlement has credited yet: the periods that ended unsettled plus the running one.
    // settledRates (period index → rate) marks the settled periods; catalogRates (period index → rate) supplies the rest.
    // Null when a period needs a catalogue rate that is missing.
    public static decimal? Unsettled(
        BondTerms terms,
        IReadOnlyDictionary<int, decimal> settledRates,
        IReadOnlyDictionary<int, decimal> catalogRates,
        DateOnly date)
    {
        if (date <= terms.PurchaseDate)
        {
            return 0m;
        }

        var schedule = BondSchedule.Periods(terms.Type, terms.PurchaseDate);
        var matured = date >= schedule[^1].End;
        var periods = matured ? schedule : [.. schedule.Take(schedule.First(p => date < p.End).Index)];

        var rates = new List<decimal>(periods.Count);
        foreach (var period in periods)
        {
            if (settledRates.TryGetValue(period.Index, out var settledRate))
            {
                rates.Add(settledRate);
            }
            else if (period.Index == 1 || BondSchedule.IsFixedRate(terms.Type))
            {
                rates.Add(terms.FirstPeriodRatePercent);
            }
            else if (catalogRates.TryGetValue(period.Index, out var catalogRate))
            {
                rates.Add(catalogRate);
            }
            else
            {
                return null;
            }
        }

        var perBond = 0m;
        foreach (var period in periods.Where(p => p.End <= date && !settledRates.ContainsKey(p.Index)))
        {
            perBond += BondInterestMath.PerBond(terms.Type, rates.GetRange(0, period.Index), period.Start, period.End);
        }

        if (!matured)
        {
            var running = periods[^1];
            perBond += BondInterestMath.AccruedPerBond(terms.Type, rates, running.Start, running.End, date)
                - BondInterestMath.CapitalisedBefore(terms.Type, rates);
        }

        return terms.BondCount * perBond;
    }
}
