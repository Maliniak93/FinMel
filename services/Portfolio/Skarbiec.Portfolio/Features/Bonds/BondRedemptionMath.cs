using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Bonds;

public sealed record BondRedemptionAmounts(
    int BondCount, decimal CapitalisedInterest, decimal DiscountIncome, decimal TaxableIncome, decimal Tax, decimal Proceeds);

/// <summary>PLN per bond: the interest accrued in the running period and the early-redemption fee taken from it.</summary>
public sealed record BondEarlyPerBond(decimal Interest, decimal Fee);

/// <summary>PLN totals for the redeemed bonds.</summary>
public sealed record BondEarlyRedemptionAmounts(
    int BondCount, decimal AccruedInterest, decimal Fee, decimal DiscountIncome, decimal TaxableIncome, decimal Tax, decimal Proceeds);

public static class BondRedemptionMath
{
    private const decimal Nominal = 100m;

    // capitalisedPerBond is C_n − 100 for a capitalising type and 0 for a coupon type, whose coupons were already paid.
    public static BondRedemptionAmounts AtMaturity(
        decimal capitalisedPerBond, decimal purchasePricePerBond, int bondCount, bool taxExempt)
    {
        var capitalised = bondCount * capitalisedPerBond;
        var discount = bondCount * (Nominal - purchasePricePerBond);
        var taxable = capitalised + discount;
        var tax = BelkaTax.On(taxable, taxExempt);

        return new BondRedemptionAmounts(
            bondCount, capitalised, discount, taxable, tax, bondCount * (Nominal + capitalisedPerBond) - tax);
    }

    // ratesPercent holds the rates of periods 1..k, k being the period the date falls in; the MF letters' WP = N × Π(1 + r_i) × (1 + r_k × a/ACT).
    public static BondEarlyPerBond EarlyPerBond(
        TreasuryBondType type,
        IReadOnlyList<decimal> ratesPercent,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly date,
        decimal feePerBond)
    {
        if (type is TreasuryBondType.Ots)
        {
            return new BondEarlyPerBond(0m, 0m);
        }

        var rate = ratesPercent[^1];
        var elapsed = (decimal)(date.DayNumber - periodStart.DayNumber);
        var length = (decimal)(periodEnd.DayNumber - periodStart.DayNumber);

        var interest = type switch
        {
            TreasuryBondType.Ror or TreasuryBondType.Dor => Round(rate * elapsed / (12m * length)),
            TreasuryBondType.Coi => Round(rate * elapsed / length),
            _ => Round(CapitalAtPeriodStart(type, ratesPercent) * (1m + rate * elapsed / (100m * length))) - Nominal,
        };

        // A coupon type's earlier coupons were paid out, so from period 2 on its fee is no longer capped at the accrued interest.
        var fee = BondSchedule.IsCoupon(type) && ratesPercent.Count > 1 ? feePerBond : Math.Min(feePerBond, interest);

        return new BondEarlyPerBond(interest, fee);
    }

    public static BondEarlyRedemptionAmounts Early(
        TreasuryBondType type,
        IReadOnlyList<decimal> ratesPercent,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly date,
        decimal feePerBond,
        decimal purchasePricePerBond,
        int bondCount,
        bool taxExempt)
    {
        var perBond = EarlyPerBond(type, ratesPercent, periodStart, periodEnd, date, feePerBond);
        var discount = bondCount * (Nominal - purchasePricePerBond);
        var taxable = Math.Max(0m, bondCount * (perBond.Interest - perBond.Fee) + discount);
        var tax = BelkaTax.On(taxable, taxExempt);

        return new BondEarlyRedemptionAmounts(
            bondCount,
            bondCount * perBond.Interest,
            bondCount * perBond.Fee,
            discount,
            taxable,
            tax,
            bondCount * (Nominal + perBond.Interest - perBond.Fee) - tax);
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
