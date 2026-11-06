namespace Skarbiec.Contracts.Bonds;

public static class BondInterestMath
{
    private const decimal Nominal = 100m;

    // ratesPercent holds the rates of periods 1..k; the last one is period k's.
    public static decimal PerBond(TreasuryBondType type, IReadOnlyList<decimal> ratesPercent, DateOnly periodStart, DateOnly periodEnd)
    {
        var rate = ratesPercent[^1];

        return type switch
        {
            TreasuryBondType.Ots => Round(Nominal * rate / 100m * (periodEnd.DayNumber - periodStart.DayNumber) / 365m),
            TreasuryBondType.Ror or TreasuryBondType.Dor => Round(Nominal * rate / 100m / 12m),
            TreasuryBondType.Coi => Round(Nominal * rate / 100m),
            _ => Capital(ratesPercent, ratesPercent.Count) - Capital(ratesPercent, ratesPercent.Count - 1),
        };
    }

    // Interest accrued per bond on the date inside period k, capitalised interest of the earlier periods included;
    // ratesPercent holds the rates of periods 1..k. The MF letters' WP = N × Π(1 + r_i) × (1 + r_k × a/ACT).
    public static decimal AccruedPerBond(
        TreasuryBondType type, IReadOnlyList<decimal> ratesPercent, DateOnly periodStart, DateOnly periodEnd, DateOnly date)
    {
        if (type is TreasuryBondType.Ots)
        {
            return PerBond(type, ratesPercent, periodStart, date);
        }

        var rate = ratesPercent[^1];
        var elapsed = (decimal)(date.DayNumber - periodStart.DayNumber);
        var length = (decimal)(periodEnd.DayNumber - periodStart.DayNumber);

        return type switch
        {
            TreasuryBondType.Ror or TreasuryBondType.Dor => Round(rate * elapsed / (12m * length)),
            TreasuryBondType.Coi => Round(rate * elapsed / length),
            _ => Round(CapitalAtPeriodStart(type, ratesPercent) * (1m + rate * elapsed / (100m * length))) - Nominal,
        };
    }

    // The interest the earlier periods of a capitalising type already added to the capital; 0 for the other types.
    public static decimal CapitalisedBefore(TreasuryBondType type, IReadOnlyList<decimal> ratesPercent) =>
        type is TreasuryBondType.Ots || BondSchedule.IsCoupon(type)
            ? 0m
            : Round(CapitalAtPeriodStart(type, ratesPercent)) - Nominal;

    // The product stays unrounded; only each period's capital is rounded, so the per-period figures sum to the final capital.
    private static decimal Capital(IReadOnlyList<decimal> ratesPercent, int periods)
    {
        var growth = 1m;
        for (var i = 0; i < periods; i++)
        {
            growth *= 1m + ratesPercent[i] / 100m;
        }

        return Round(Nominal * growth);
    }

    // C*_{k−1}: exact for EDO/ROS/ROD; the TOS letter rounds each period's capital to grosze.
    private static decimal CapitalAtPeriodStart(TreasuryBondType type, IReadOnlyList<decimal> ratesPercent)
    {
        var capital = Nominal;
        for (var i = 0; i < ratesPercent.Count - 1; i++)
        {
            capital *= 1m + ratesPercent[i] / 100m;
            if (type is TreasuryBondType.Tos)
            {
                capital = Round(capital);
            }
        }

        return capital;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
