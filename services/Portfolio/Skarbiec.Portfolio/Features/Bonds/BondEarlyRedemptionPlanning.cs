using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Bonds;

internal sealed record BondEarlyRedemptionPlan(
    Asset Bond, TreasuryBond Terms, DateOnly Date, BondPeriod Period, decimal CapitalisedInterest, BondEarlyRedemptionAmounts Amounts)
{
    // The credit lifts the redeemed bonds' book value (price + capitalised) to 100 + accrued; the charge takes the fee and the tax.
    public (Transaction? Credit, Transaction? Charge) CreateAdjustments()
    {
        var credit = Amounts.AccruedInterest - CapitalisedInterest + Amounts.DiscountIncome;
        var charge = Amounts.Fee + Amounts.Tax + Math.Max(0m, -credit);

        return (
            credit > 0 ? NewTransaction(TransactionType.Deposit, credit) : null,
            charge > 0 ? NewTransaction(TransactionType.Withdraw, charge) : null);
    }

    private Transaction NewTransaction(TransactionType type, decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = Bond.Id,
        Type = type,
        Quantity = amount,
        UnitPriceAmount = 1m,
        Date = Date
    };
}

internal static class BondEarlyRedemptionPlanning
{
    public static async Task<Result<BondEarlyRedemptionPlan>> PlanEarlyRedemptionAsync(
        this PortfolioDbContext dbContext,
        Guid portfolioId,
        Guid assetId,
        DateOnly date,
        int bondCount,
        decimal? runningPeriodRatePercent,
        DateOnly today,
        bool forWrite,
        CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's bond, a non-bond asset and a wrong portfolio all end in 404.
        var bond = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Bond, cancellationToken);
        var terms = bond is null
            ? null
            : await dbContext.TreasuryBonds.FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (bond is null || terms is null)
        {
            return BondErrors.NotFound(assetId);
        }

        if (forWrite && await dbContext.ReadOnlyErrorAsync(bond, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        var redemptions = await dbContext.BondRedemptions
            .AsNoTracking()
            .Where(r => r.AssetId == assetId)
            .Select(r => new { r.Kind, r.Date })
            .ToListAsync(cancellationToken);

        if (terms.BondCount == 0 || redemptions.Any(r => r.Kind != BondRedemptionKind.Early))
        {
            return BondErrors.AlreadyRedeemed;
        }

        if (date <= terms.PurchaseDate || date >= terms.MaturityDate || date > today)
        {
            return BondErrors.EarlyRedemptionDate;
        }

        if (bondCount < 1 || bondCount > terms.BondCount)
        {
            return BondErrors.RedemptionCount;
        }

        var period = BondSchedule.Periods(terms.Type, terms.PurchaseDate).First(p => p.Start <= date && date < p.End);
        var usesTermsRate = BondSchedule.IsFixedRate(terms.Type) || period.Index == 1;
        if (usesTermsRate
                ? runningPeriodRatePercent is not null
                : !BondInterestPlanning.IsValidRate(runningPeriodRatePercent))
        {
            return BondErrors.PeriodRate;
        }

        var settlements = await dbContext.BondInterestSettlements
            .AsNoTracking()
            .Where(s => s.AssetId == assetId)
            .OrderBy(s => s.PeriodIndex)
            .Select(s => new { s.RatePercent, s.GrossInterest, s.BondCount })
            .ToListAsync(cancellationToken);

        // Every period ending on or before the date is settled, and none after it, so the rates are exactly periods 1..k−1.
        if (settlements.Count < period.Index - 1)
        {
            return BondErrors.InterestUnsettled;
        }

        if (settlements.Count > period.Index - 1 || redemptions.Any(r => r.Date > date))
        {
            return BondErrors.EarlyRedemptionOutOfOrder;
        }

        List<decimal> rates =
        [
            .. settlements.Select(s => s.RatePercent),
            usesTermsRate ? terms.FirstPeriodRatePercent : runningPeriodRatePercent!.Value,
        ];

        var amounts = BondRedemptionMath.Early(
            terms.Type,
            rates,
            period.Start,
            period.End,
            date,
            terms.EarlyRedemptionFeePerBond,
            terms.PurchasePricePerBond,
            bondCount,
            terms.TaxExempt);

        // c_{k−1}: the interest already credited to each bond; 0 for a coupon type, whose coupons left for Cash.
        var capitalisedPerBond = BondSchedule.IsCoupon(terms.Type)
            ? 0m
            : settlements.Sum(s => s.GrossInterest / s.BondCount);

        return new BondEarlyRedemptionPlan(bond, terms, date, period, bondCount * capitalisedPerBond, amounts);
    }
}
