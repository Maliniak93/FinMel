using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds;

internal sealed record PlannedBondPeriod(BondPeriod Period, decimal RatePercent, int BondCount, BondInterestAmounts Amounts);

internal sealed record BondInterestPlan(
    Asset Bond, TreasuryBond Terms, IReadOnlyList<PlannedBondPeriod> Periods, Asset? Destination);

internal static class BondInterestPlanning
{
    public static async Task<Result<BondInterestPlan>> PlanBondInterestAsync(
        this PortfolioDbContext dbContext,
        Guid portfolioId,
        Guid assetId,
        SettleBondInterestRequest request,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's bond, a non-bond asset and a wrong portfolio all end in 404.
        var bond = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Bond, cancellationToken);
        var terms = bond is null
            ? null
            : await dbContext.TreasuryBonds.AsNoTracking().FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (bond is null || terms is null)
        {
            return BondErrors.NotFound(assetId);
        }

        if (await dbContext.ReadOnlyErrorAsync(bond, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        // Capitalising math compounds every earlier period's rate, so the stored ones lead the list.
        var rates = await dbContext.BondInterestSettlements
            .AsNoTracking()
            .Where(s => s.AssetId == assetId)
            .OrderBy(s => s.PeriodIndex)
            .Select(s => s.RatePercent)
            .ToListAsync(cancellationToken);

        var schedule = BondSchedule.Periods(terms.Type, terms.PurchaseDate);
        var nextIndex = rates.Count + 1;
        var requested = request.Periods ?? [];

        if (requested.Count == 0
            || nextIndex + requested.Count - 1 > schedule.Count
            || requested.Where((p, i) => p.PeriodIndex != nextIndex + i).Any())
        {
            return BondErrors.InterestPeriodMismatch(nextIndex);
        }

        var periods = requested.Select(p => schedule[p.PeriodIndex - 1]).ToList();
        if (periods.Any(p => p.End > today))
        {
            return BondErrors.InterestNotDue;
        }

        var fixedRate = BondSchedule.IsFixedRate(terms.Type);
        foreach (var period in requested)
        {
            var usesTermsRate = fixedRate || period.PeriodIndex == 1;
            if (usesTermsRate ? period.RatePercent is not null : !IsValidRate(period.RatePercent))
            {
                return BondErrors.PeriodRate;
            }
        }

        var coupon = BondSchedule.IsCoupon(terms.Type);
        if (coupon && request.DestinationAssetId is null)
        {
            return BondErrors.PayoutDestinationRequired;
        }

        if (!coupon && request.DestinationAssetId is not null)
        {
            return BondErrors.PayoutDestinationNotAllowed;
        }

        Asset? destination = null;
        if (request.DestinationAssetId is { } destinationAssetId)
        {
            destination = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == destinationAssetId, cancellationToken);

            // Bond → Bond is a swap's route only, so a coupon is paid to Cash alone.
            if (destination is null
                || destination.AssetClass != AssetClass.Cash
                || !TransferRoutes.IsAllowed(bond.AssetClass, destination.AssetClass)
                || destination.Currency != bond.Currency
                || destination.IsArchived
                || await dbContext.IsPortfolioArchivedAsync(destination.PortfolioId, cancellationToken))
            {
                return TransferErrors.InvalidCounterpart;
            }
        }

        var planned = new List<PlannedBondPeriod>(periods.Count);
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            var rate = fixedRate || period.Index == 1 ? terms.FirstPeriodRatePercent : requested[i].RatePercent!.Value;
            rates.Add(rate);

            var amounts = BondInterestMath.Settle(
                terms.Type, rates, period.Start, period.End, terms.BondCount, terms.TaxExempt);
            planned.Add(new PlannedBondPeriod(period, rate, terms.BondCount, amounts));
        }

        return new BondInterestPlan(bond, terms, planned, destination);
    }

    // Stored as numeric(7,4).
    private static bool IsValidRate(decimal? ratePercent) =>
        ratePercent is { } rate && rate >= 0m && rate <= 100m && decimal.Round(rate, 4) == rate;
}
