using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds.RedeemBond;

public sealed class RedeemBondHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    TimeProvider timeProvider)
{
    public async Task<Result<BondResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, RedeemBondRequest request, CancellationToken cancellationToken)
    {
        var today = WarsawCalendar.Today(timeProvider);
        var planned = await dbContext.PlanRedemptionAsync(portfolioId, assetId, today, forWrite: true, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error;
        }

        var plan = planned.Value;
        var bond = plan.Bond;

        var destination = await dbContext.LoadCashDestinationAsync(bond, request.DestinationAssetId, cancellationToken);
        if (destination.IsFailure)
        {
            return destination.Error;
        }

        var cash = destination.Value;
        var (credit, charge) = plan.CreateAdjustments();
        var (outLeg, inLeg) = TransferLegs.Create(bond.Id, cash.Id, plan.Amounts.Proceeds, plan.Date);
        List<Transaction> bondAdded = [.. new[] { credit, charge }.OfType<Transaction>(), outLeg];

        var bondQuantity = await dbContext.RecomputeWithAsync(bond.Id, bondAdded, cancellationToken);
        if (bondQuantity.IsFailure)
        {
            return bondQuantity.Error;
        }

        var cashQuantity = await dbContext.RecomputeWithAsync(cash.Id, [inLeg], cancellationToken);
        if (cashQuantity.IsFailure)
        {
            return cashQuantity.Error;
        }

        // Both sides are PLN: the frozen PLN rate is 1 by definition, so MarketData is not asked.
        foreach (var transaction in bondAdded.Append(inLeg))
        {
            transaction.FxRateToPln = 1m;
            dbContext.Transactions.Add(transaction);
        }

        dbContext.BondRedemptions.Add(new BondRedemption
        {
            Id = Guid.NewGuid(),
            AssetId = bond.Id,
            Kind = BondRedemptionKind.Maturity,
            Date = plan.Date,
            BondCount = plan.Amounts.BondCount,
            CapitalisedInterest = plan.Amounts.CapitalisedInterest,
            DiscountIncome = plan.Amounts.DiscountIncome,
            Tax = plan.Amounts.Tax,
            Proceeds = plan.Amounts.Proceeds,
            CreditTransactionId = credit?.Id,
            ChargeTransactionId = charge?.Id,
            CashTransferId = outLeg.TransferId
        });

        bond.Quantity = bondQuantity.Value;
        cash.Quantity = cashQuantity.Value;

        // Published before SaveChangesAsync so both outbox rows commit with the redemption.
        await positionEventPublisher.PublishChangedAsync(bond, cancellationToken);
        await positionEventPublisher.PublishChangedAsync(cash, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return (await dbContext.LoadBondAsync(portfolioId, assetId, today, cancellationToken))!;
    }
}
