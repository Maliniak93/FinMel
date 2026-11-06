using Skarbiec.Contracts;
using Skarbiec.Contracts.Bonds;

namespace Skarbiec.Portfolio.Features.Bonds;

public sealed record BondInterestAmounts(decimal Gross, decimal Tax, decimal Net);

public static class BondSettlementMath
{
    // Capitalised interest is taxed at redemption, not when it is credited.
    public static BondInterestAmounts Settle(
        TreasuryBondType type,
        IReadOnlyList<decimal> ratesPercent,
        DateOnly periodStart,
        DateOnly periodEnd,
        int bondCount,
        bool taxExempt)
    {
        var gross = BondInterestMath.PerBond(type, ratesPercent, periodStart, periodEnd) * bondCount;
        var tax = BondSchedule.IsCoupon(type) ? BelkaTax.On(gross, taxExempt) : 0m;

        return new BondInterestAmounts(gross, tax, gross - tax);
    }
}
