using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Bonds;

public enum BondEstimateUnavailableReason
{
    RateMissing,

    MarketDataUnavailable,
}

public sealed record BondEstimateResponse
{
    /// <summary>PLN for the whole holding: nominal plus the interest accrued to AsOf and the coupons not settled yet.</summary>
    public required decimal GrossValue { get; init; }

    /// <summary>PLN an early redemption of the whole holding on AsOf would pay after the fee and Belka tax, plus the unsettled coupons after tax; for a matured bond, what redemption at maturity pays.</summary>
    public required decimal NetValue { get; init; }

    public required DateOnly AsOf { get; init; }
}

public sealed record BondValuationOutcome(BondEstimateResponse? Estimate, BondEstimateUnavailableReason? Reason)
{
    public static BondValuationOutcome None { get; } = new(null, null);

    public static BondValuationOutcome Unavailable(BondEstimateUnavailableReason reason) => new(null, reason);
}

public static class BondValuation
{
    private const decimal Nominal = 100m;

    public static bool NeedsCatalogRates(TreasuryBond bond, IReadOnlyCollection<BondInterestSettlement> settlements, DateOnly today)
    {
        if (bond.BondCount == 0)
        {
            return false;
        }

        var settled = settlements.Select(s => s.PeriodIndex).ToHashSet();

        return PeriodsToDate(bond, today).Any(p => UsesCatalogRate(bond, p) && !settled.Contains(p.Index));
    }

    // catalogRates is keyed by the 1-based period index, as BondPeriod.Index.
    public static BondValuationOutcome Today(
        TreasuryBond bond,
        IReadOnlyCollection<BondInterestSettlement> settlements,
        IReadOnlyDictionary<int, decimal> catalogRates,
        DateOnly today)
    {
        if (bond.BondCount == 0)
        {
            return BondValuationOutcome.None;
        }

        var settled = settlements.ToDictionary(s => s.PeriodIndex);
        var periods = PeriodsToDate(bond, today);
        var rates = new List<decimal>(periods.Count);
        foreach (var period in periods)
        {
            if (settled.TryGetValue(period.Index, out var settlement))
            {
                rates.Add(settlement.RatePercent);
            }
            else if (!UsesCatalogRate(bond, period))
            {
                rates.Add(bond.FirstPeriodRatePercent);
            }
            else if (catalogRates.TryGetValue(period.Index, out var catalogRate))
            {
                rates.Add(catalogRate);
            }
            else
            {
                return BondValuationOutcome.Unavailable(BondEstimateUnavailableReason.RateMissing);
            }
        }

        var count = bond.BondCount;
        var isCoupon = BondSchedule.IsCoupon(bond.Type);

        var unsettledCoupons = isCoupon
            ? periods
                .Where(p => p.End <= today && !settled.ContainsKey(p.Index))
                .Select(p => BondInterestMath.Settle(bond.Type, rates.GetRange(0, p.Index), p.Start, p.End, count, bond.TaxExempt))
                .ToList()
            : [];
        var couponsPerBond = unsettledCoupons.Sum(c => c.Gross) / count;
        var couponsNet = unsettledCoupons.Sum(c => c.Net);

        decimal interestPerBond;
        decimal redemptionProceeds;
        if (today >= bond.MaturityDate)
        {
            // A coupon type's every coupon is counted above; a capitalising type's interest is all paid at maturity.
            interestPerBond = isCoupon
                ? 0m
                : periods.Sum(p => BondInterestMath.PerBond(bond.Type, rates.GetRange(0, p.Index), p.Start, p.End));
            redemptionProceeds = BondRedemptionMath.AtMaturity(interestPerBond, bond.PurchasePricePerBond, count, bond.TaxExempt).Proceeds;
        }
        else
        {
            var running = periods[^1];
            var date = today < bond.PurchaseDate ? bond.PurchaseDate : today;
            var early = BondRedemptionMath.Early(
                bond.Type,
                rates,
                running.Start,
                running.End,
                date,
                bond.EarlyRedemptionFeePerBond,
                bond.PurchasePricePerBond,
                count,
                bond.TaxExempt);

            // Early redemption of an OTS pays no interest, yet it still accrues day by day.
            interestPerBond = bond.Type is TreasuryBondType.Ots
                ? BondInterestMath.PerBond(bond.Type, rates, running.Start, date)
                : early.AccruedInterest / count;
            redemptionProceeds = early.Proceeds;
        }

        var grossPerBond = Math.Round(Nominal + interestPerBond + couponsPerBond, 2, MidpointRounding.AwayFromZero);

        return new BondValuationOutcome(
            new BondEstimateResponse
            {
                GrossValue = count * grossPerBond,
                NetValue = redemptionProceeds + couponsNet,
                AsOf = today
            },
            null);
    }

    // Periods 1..k, k being the period running on the date; every period once the bond matured.
    private static IReadOnlyList<BondPeriod> PeriodsToDate(TreasuryBond bond, DateOnly today)
    {
        var periods = BondSchedule.Periods(bond.Type, bond.PurchaseDate);
        if (today >= bond.MaturityDate)
        {
            return periods;
        }

        var running = periods.First(p => today < p.End);

        return [.. periods.Take(running.Index)];
    }

    private static bool UsesCatalogRate(TreasuryBond bond, BondPeriod period) =>
        !BondSchedule.IsFixedRate(bond.Type) && period.Index != 1;
}
