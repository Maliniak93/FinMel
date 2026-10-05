namespace Skarbiec.Portfolio.Features.Bonds;

public sealed record BondRedemptionAmounts(
    int BondCount, decimal CapitalisedInterest, decimal DiscountIncome, decimal TaxableIncome, decimal Tax, decimal Proceeds);

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
}
