using Skarbiec.Contracts;
using Skarbiec.Contracts.Bonds;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Bonds;

public enum BondEstimateUnavailableReason
{
    RateMissing,

    MarketDataUnavailable,
}

public sealed record BondEstimateResponse
{
    /// <summary>PLN for the whole holding: book value plus the gross interest not settled yet as of AsOf.</summary>
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
        decimal bookValue,
        bool taxExempt,
        IReadOnlyCollection<BondInterestSettlement> settlements,
        IReadOnlyDictionary<int, decimal> catalogRates,
        DateOnly today)
    {
        if (bond.BondCount == 0)
        {
            return BondValuationOutcome.None;
        }

        var settled = settlements.ToDictionary(s => s.PeriodIndex);
        var settledRates = settlements.ToDictionary(s => s.PeriodIndex, s => s.RatePercent);
        var terms = new BondTerms(bond.Type, bond.PurchaseDate, bond.BondCount, bond.FirstPeriodRatePercent);
        if (BondAccrual.Unsettled(terms, settledRates, catalogRates, today) is not { } unsettledGross)
        {
            return BondValuationOutcome.Unavailable(BondEstimateUnavailableReason.RateMissing);
        }

        var periods = PeriodsToDate(bond, today);
        var rates = periods
            .Select(p => settledRates.TryGetValue(p.Index, out var rate) ? rate
                : UsesCatalogRate(bond, p) ? catalogRates[p.Index]
                : bond.FirstPeriodRatePercent)
            .ToList();

        var count = bond.BondCount;
        var unsettledCoupons = BondSchedule.IsCoupon(bond.Type)
            ? periods
                .Where(p => p.End <= today && !settled.ContainsKey(p.Index))
                .Select(p => BondSettlementMath.Settle(bond.Type, rates.GetRange(0, p.Index), p.Start, p.End, count, taxExempt))
                .ToList()
            : [];
        var couponsNet = unsettledCoupons.Sum(c => c.Net);

        decimal redemptionProceeds;
        if (today >= bond.MaturityDate)
        {
            // A coupon type's every coupon is counted above; a capitalising type's interest is all paid at maturity.
            var interestPerBond = BondSchedule.IsCoupon(bond.Type)
                ? 0m
                : periods.Sum(p => BondInterestMath.PerBond(bond.Type, rates.GetRange(0, p.Index), p.Start, p.End));
            redemptionProceeds = BondRedemptionMath.AtMaturity(interestPerBond, bond.PurchasePricePerBond, count, taxExempt).Proceeds;
        }
        else
        {
            var running = periods[^1];
            var date = today < bond.PurchaseDate ? bond.PurchaseDate : today;
            redemptionProceeds = BondRedemptionMath.Early(
                bond.Type,
                rates,
                running.Start,
                running.End,
                date,
                bond.EarlyRedemptionFeePerBond,
                bond.PurchasePricePerBond,
                count,
                taxExempt).Proceeds;
        }

        return new BondValuationOutcome(
            new BondEstimateResponse
            {
                GrossValue = bookValue + unsettledGross,
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
