using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds.SwapBond;

public sealed class SwapBondHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    TimeProvider timeProvider)
{
    private const string Currency = "PLN";

    public async Task<Result<BondResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, SwapBondRequest request, CancellationToken cancellationToken)
    {
        var today = WarsawCalendar.Today(timeProvider);
        var planned = await dbContext.PlanRedemptionAsync(portfolioId, assetId, today, forWrite: true, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error;
        }

        var plan = planned.Value;
        var (bond, terms, amounts) = plan;
        var newBond = request.NewBond;

        if (request.BondCount < 1 || request.BondCount > terms.BondCount)
        {
            return BondErrors.SwapCount;
        }

        var cost = request.BondCount * newBond.SwapPricePerBond;
        var leftover = amounts.Proceeds - cost;
        if (leftover < 0)
        {
            return BondErrors.SwapExceedsProceeds;
        }

        Asset? cash = null;
        if (leftover > 0)
        {
            if (request.DestinationAssetId is not { } destinationAssetId)
            {
                return BondErrors.SwapLeftoverDestinationRequired;
            }

            var destination = await dbContext.LoadCashDestinationAsync(bond, destinationAssetId, cancellationToken);
            if (destination.IsFailure)
            {
                return destination.Error;
            }

            cash = destination.Value;
        }

        // The swap stays in the old bond's portfolio, so an IKE lot stays in IKE with its tax exemption.
        var portfolio = await dbContext.Portfolios.FirstAsync(p => p.Id == portfolioId, cancellationToken);
        var swapped = new Asset
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            AssetClass = AssetClass.Bond,
            Name = newBond.Name,
            Currency = Currency,
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        var (credit, charge) = plan.CreateAdjustments();
        var (swapOut, swapIn) = TransferLegs.Create(bond.Id, swapped.Id, cost, plan.Date);
        Transaction? cashOut = null;
        Transaction? cashIn = null;
        if (cash is not null)
        {
            (cashOut, cashIn) = TransferLegs.Create(bond.Id, cash.Id, leftover, plan.Date);
        }

        List<Transaction> bondAdded = [.. new[] { credit, charge, swapOut, cashOut }.OfType<Transaction>()];

        var bondQuantity = await dbContext.RecomputeWithAsync(bond.Id, bondAdded, cancellationToken);
        if (bondQuantity.IsFailure)
        {
            return bondQuantity.Error;
        }

        var swappedQuantity = TransactionQuantityCalculator.Recompute([swapIn]);
        if (swappedQuantity.IsFailure)
        {
            return swappedQuantity.Error;
        }

        decimal? cashQuantity = null;
        if (cash is not null && cashIn is not null)
        {
            var recomputed = await dbContext.RecomputeWithAsync(cash.Id, [cashIn], cancellationToken);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            cashQuantity = recomputed.Value;
        }

        // Every side is PLN: the frozen PLN rate is 1 by definition, so MarketData is not asked.
        Transaction?[] added = [.. bondAdded, swapIn, cashIn];
        foreach (var transaction in added.OfType<Transaction>())
        {
            transaction.FxRateToPln = 1m;
            dbContext.Transactions.Add(transaction);
        }

        dbContext.Assets.Add(swapped);
        dbContext.TreasuryBonds.Add(new TreasuryBond
        {
            AssetId = swapped.Id,
            SeriesCode = newBond.SeriesCode,
            Type = newBond.Type,
            PurchaseDate = plan.Date,
            BondCount = request.BondCount,
            PurchasePricePerBond = newBond.SwapPricePerBond,
            FirstPeriodRatePercent = newBond.FirstPeriodRatePercent,
            MarginPercent = newBond.MarginPercent,
            EarlyRedemptionFeePerBond = newBond.EarlyRedemptionFeePerBond,
            TaxExempt = terms.TaxExempt,
            MaturityDate = BondSchedule.MaturityDate(newBond.Type, plan.Date),
            SwappedFromAssetId = bond.Id
        });

        dbContext.BondRedemptions.Add(new BondRedemption
        {
            Id = Guid.NewGuid(),
            AssetId = bond.Id,
            Kind = BondRedemptionKind.Swap,
            Date = plan.Date,
            BondCount = amounts.BondCount,
            CapitalisedInterest = amounts.CapitalisedInterest,
            DiscountIncome = amounts.DiscountIncome,
            Tax = amounts.Tax,
            Proceeds = amounts.Proceeds,
            CreditTransactionId = credit?.Id,
            ChargeTransactionId = charge?.Id,
            CashTransferId = cashOut?.TransferId,
            SwapTargetAssetId = swapped.Id,
            SwapTransferId = swapOut.TransferId
        });

        bond.Quantity = bondQuantity.Value;
        swapped.Quantity = swappedQuantity.Value;

        // All three assets publish in the same save as every leg and the redemption.
        await positionEventPublisher.PublishChangedAsync(bond, cancellationToken);
        await positionEventPublisher.PublishCreatedAsync(swapped, portfolio, cancellationToken);

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

        return (await dbContext.LoadBondAsync(portfolioId, swapped.Id, today, cancellationToken))!;
    }
}
