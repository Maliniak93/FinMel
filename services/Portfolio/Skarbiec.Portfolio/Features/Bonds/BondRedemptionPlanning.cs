using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds;

internal sealed record BondRedemptionPlan(Asset Bond, TreasuryBond Terms, BondRedemptionAmounts Amounts)
{
    // Everything is dated the maturity date, however late the redemption is recorded.
    public DateOnly Date => Terms.MaturityDate;

    // The discount credit and the tax charge take the balance from cost + capitalised interest to exactly the proceeds.
    public (Transaction? Credit, Transaction? Charge) CreateAdjustments() => (
        Amounts.DiscountIncome > 0 ? NewTransaction(TransactionType.Deposit, Amounts.DiscountIncome) : null,
        Amounts.Tax > 0 ? NewTransaction(TransactionType.Withdraw, Amounts.Tax) : null);

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

internal static class BondRedemptionPlanning
{
    public static async Task<Result<BondRedemptionPlan>> PlanRedemptionAsync(
        this PortfolioDbContext dbContext,
        Guid portfolioId,
        Guid assetId,
        DateOnly today,
        bool forWrite,
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

        if (forWrite && await dbContext.ReadOnlyErrorAsync(bond, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        if (terms.MaturityDate > today)
        {
            return BondErrors.NotMatured;
        }

        var settlements = await dbContext.BondInterestSettlements
            .AsNoTracking()
            .Where(s => s.AssetId == assetId)
            .Select(s => new { s.GrossInterest, s.BondCount })
            .ToListAsync(cancellationToken);

        if (settlements.Count < BondSchedule.Periods(terms.Type, terms.PurchaseDate).Count)
        {
            return BondErrors.InterestUnsettled;
        }

        if (await dbContext.BondRedemptions.AnyAsync(r => r.AssetId == assetId, cancellationToken))
        {
            return BondErrors.AlreadyRedeemed;
        }

        // Summing the per-bond credits gives C_n − 100 for a compounding type and the single period's interest for OTS.
        var capitalisedPerBond = BondSchedule.IsCoupon(terms.Type)
            ? 0m
            : settlements.Sum(s => s.GrossInterest / s.BondCount);

        var amounts = BondRedemptionMath.AtMaturity(
            capitalisedPerBond, terms.PurchasePricePerBond, terms.BondCount, terms.TaxExempt);

        return new BondRedemptionPlan(bond, terms, amounts);
    }

    public static async Task<Result<Asset>> LoadCashDestinationAsync(
        this PortfolioDbContext dbContext, Asset bond, Guid destinationAssetId, CancellationToken cancellationToken)
    {
        var destination = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == destinationAssetId, cancellationToken);

        if (destination is null
            || destination.AssetClass != AssetClass.Cash
            || !TransferRoutes.IsAllowed(bond.AssetClass, destination.AssetClass)
            || destination.Currency != bond.Currency
            || destination.IsArchived
            || await dbContext.IsPortfolioArchivedAsync(destination.PortfolioId, cancellationToken))
        {
            return TransferErrors.InvalidCounterpart;
        }

        return destination;
    }

    public static async Task<Result<decimal>> RecomputeWithAsync(
        this PortfolioDbContext dbContext, Guid assetId, IReadOnlyCollection<Transaction> added, CancellationToken cancellationToken)
    {
        var history = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        return TransactionQuantityCalculator.Recompute([.. history, .. added]);
    }
}
