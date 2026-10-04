using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Bonds;

public sealed record BondInterestAmounts(decimal Gross, decimal Tax, decimal Net);

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

    // Capitalised interest is taxed at redemption, not when it is credited.
    public static BondInterestAmounts Settle(
        TreasuryBondType type,
        IReadOnlyList<decimal> ratesPercent,
        DateOnly periodStart,
        DateOnly periodEnd,
        int bondCount,
        bool taxExempt)
    {
        var gross = PerBond(type, ratesPercent, periodStart, periodEnd) * bondCount;
        var tax = BondSchedule.IsCoupon(type) ? BelkaTax.On(gross, taxExempt) : 0m;

        return new BondInterestAmounts(gross, tax, gross - tax);
    }

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

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
