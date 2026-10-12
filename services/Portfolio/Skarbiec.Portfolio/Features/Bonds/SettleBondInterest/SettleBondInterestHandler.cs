using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;

public sealed class SettleBondInterestHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<BondSettlementResponse>>> HandleAsync(
        Guid assetId, SettleBondInterestRequest request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.PlanBondInterestAsync(
            assetId, request, WarsawCalendar.Today(timeProvider), cancellationToken);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        var (bond, terms, periods, cash) = plan.Value;
        var coupon = BondSchedule.IsCoupon(terms.Type);

        var bondHistory = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == bond.Id)
            .ToListAsync(cancellationToken);
        var bondAdded = new List<Transaction>();
        var cashAdded = new List<Transaction>();
        var settlements = new List<BondInterestSettlement>(periods.Count);

        foreach (var planned in periods)
        {
            // A coupon is credited net and paid straight out, so the bond keeps its value; capitalised interest stays in gross.
            var creditAmount = coupon ? planned.Amounts.Net : planned.Amounts.Gross;
            Transaction? credit = null;
            Guid? transferId = null;

            if (creditAmount > 0)
            {
                credit = new Transaction
                {
                    Id = Guid.NewGuid(),
                    AssetId = bond.Id,
                    Type = TransactionType.Deposit,
                    Quantity = creditAmount,
                    UnitPriceAmount = 1m,
                    Date = planned.Period.End
                };
                bondAdded.Add(credit);

                if (coupon && cash is not null)
                {
                    var (outLeg, inLeg) = TransferLegs.Create(bond.Id, cash.Id, creditAmount, planned.Period.End);
                    bondAdded.Add(outLeg);
                    cashAdded.Add(inLeg);
                    transferId = outLeg.TransferId;
                }
            }

            settlements.Add(new BondInterestSettlement
            {
                Id = Guid.NewGuid(),
                AssetId = bond.Id,
                PeriodIndex = planned.Period.Index,
                PeriodStart = planned.Period.Start,
                PeriodEnd = planned.Period.End,
                RatePercent = planned.RatePercent,
                BondCount = planned.BondCount,
                GrossInterest = planned.Amounts.Gross,
                Tax = planned.Amounts.Tax,
                CreditTransactionId = credit?.Id,
                TransferId = transferId
            });
        }

        var bondQuantity = TransactionQuantityCalculator.Recompute([.. bondHistory, .. bondAdded]);
        if (bondQuantity.IsFailure)
        {
            return bondQuantity.Error;
        }

        decimal? cashQuantity = null;
        if (cash is not null && cashAdded.Count > 0)
        {
            var cashHistory = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId == cash.Id)
                .ToListAsync(cancellationToken);

            var recomputed = TransactionQuantityCalculator.Recompute([.. cashHistory, .. cashAdded]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            cashQuantity = recomputed.Value;
        }

        // Both sides are PLN: the frozen PLN rate is 1 by definition, so MarketData is not asked.
        foreach (var transaction in bondAdded.Concat(cashAdded))
        {
            transaction.FxRateToPln = 1m;
            dbContext.Transactions.Add(transaction);
        }

        dbContext.BondInterestSettlements.AddRange(settlements);
        bond.Quantity = bondQuantity.Value;

        // The version bump makes the asset row's xmin guard a racing second settlement.
        await positionEventPublisher.PublishChangedAsync(bond, cancellationToken);

        if (cash is not null && cashQuantity is { } quantity)
        {
            cash.Quantity = quantity;
            await positionEventPublisher.PublishChangedAsync(cash, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return settlements.Select(s => s.ToResponse()).ToList();
    }
}
