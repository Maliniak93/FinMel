using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.CashPlaces;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.RecordTransaction;

public sealed class RecordTransactionHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, IFxRateLookupClient fxRateLookupClient)
{
    public async Task<Result<TransactionResponse>> HandleAsync(
        Guid assetId, RecordTransactionRequest request, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        if (await dbContext.ReadOnlyErrorAsync(asset, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        // A term deposit's transactions are system-managed by the deposit slices.
        if (asset.AssetClass == AssetClass.Deposit)
        {
            return DepositErrors.TransactionsManaged;
        }

        // A treasury bond's only transaction is its opening one, rewritten by UpdateBond.
        if (asset.AssetClass == AssetClass.Bond)
        {
            return BondErrors.TransactionsManaged;
        }

        if (!AssetTransactionTypes.IsAllowed(asset.AssetClass, request.Type))
        {
            return TransactionErrors.TypeNotAllowedForClass(request.Type, asset.AssetClass);
        }

        if (request.CashAssetId is not null && asset.AssetClass != AssetClass.PreciousMetal)
        {
            return TransferErrors.CashLinkNotAllowed;
        }

        if (BrokerageTrade.FeeError(asset.AssetClass, request.Type, request.FeeAmount) is { } feeError)
        {
            return feeError;
        }

        if (BrokerageTrade.AmountPlnError(
            asset.AssetClass, asset.Currency, request.Type, request.Quantity, request.UnitPrice, request.AmountPln) is { } amountPlnError)
        {
            return amountPlnError;
        }

        var unitPrice = Money.Create(request.UnitPrice, asset.Currency);
        if (unitPrice.IsFailure)
        {
            return unitPrice.Error;
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            Type = request.Type,
            Quantity = request.Quantity,
            UnitPriceAmount = BrokerageTrade.Applies(asset.AssetClass)
                ? BrokerageTrade.SecurityUnitPrice(request.Type, unitPrice.Value.Amount)
                : unitPrice.Value.Amount,
            FeeAmount = request.FeeAmount,
            AmountPln = request.AmountPln,
            Date = request.Date
        };

        // Recompute over the full history rather than incrementing in place: the one path shared with edit and delete.
        var existingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        var recomputed = TransactionQuantityCalculator.Recompute(
            [.. existingTransactions, transaction], asset.OversellError(TransactionErrors.OversellsPosition));
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        BrokerageLegs? brokerage = null;
        CashLeg? cashLeg = null;
        if (BrokerageTrade.Applies(asset.AssetClass))
        {
            var planned = await dbContext.PlanAsync(fxRateLookupClient, asset, transaction, cancellationToken);
            if (planned.IsFailure)
            {
                return planned.Error;
            }

            brokerage = planned.Value;
        }
        else
        {
            if (request.CashAssetId is { } cashAssetId)
            {
                var planned = await PlanCashLegAsync(cashAssetId, asset, transaction, cancellationToken);
                if (planned.IsFailure)
                {
                    return planned.Error;
                }

                cashLeg = planned.Value;
            }

            // Last check before the write: only a valid request on an active asset asks MarketData.
            var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.Date, cancellationToken);
            if (fxRateToPln.IsFailure)
            {
                return fxRateToPln.Error;
            }

            transaction.FxRateToPln = fxRateToPln.Value;
            if (cashLeg is not null)
            {
                // Same currency on both sides: one lookup freezes the rate on both legs.
                cashLeg.Leg.FxRateToPln = fxRateToPln.Value;
            }
        }

        asset.Quantity = recomputed.Value;
        dbContext.Transactions.Add(transaction);

        // The event carries the recomputed quantity, so no consumer has to replay the history.
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        if (brokerage is not null)
        {
            brokerage.Cash.Quantity = brokerage.CashQuantity;
            dbContext.Transactions.Add(brokerage.CashLeg);
            await positionEventPublisher.PublishChangedAsync(brokerage.Cash, cancellationToken);
        }

        if (cashLeg is not null)
        {
            cashLeg.Cash.Quantity = cashLeg.CashQuantity;
            dbContext.Transactions.Add(cashLeg.Leg);

            // The Cash publishes in the same save as both legs and the metal's own event.
            await positionEventPublisher.PublishChangedAsync(cashLeg.Cash, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A write racing on either asset moved its xmin: nothing is saved, the user retries.
            return TransactionErrors.ConcurrentModification();
        }

        return transaction.ToResponse(
            asset.Currency, brokerage?.ToTransferResponse(transaction) ?? cashLeg?.ToTransferResponse(asset, transaction));
    }

    private async Task<Result<CashLeg>> PlanCashLegAsync(
        Guid cashAssetId, Asset metal, Transaction metalLeg, CancellationToken cancellationToken)
    {
        // Through the tenancy filter: a stranger's Cash gets the same 400 as any unsuitable counterpart.
        var cash = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == cashAssetId, cancellationToken);
        var isBuy = metalLeg.Type == TransactionType.Buy;

        if (cash is null
            || !cash.IsCashPlaceBalance()
            || !(isBuy ? TransferRoutes.IsAllowed(cash.AssetClass, metal.AssetClass) : TransferRoutes.IsAllowed(metal.AssetClass, cash.AssetClass))
            || cash.Currency != metal.Currency
            || cash.IsArchived)
        {
            return TransferErrors.InvalidCounterpart;
        }

        if (await dbContext.ReadOnlyErrorAsync(cash, cancellationToken) is { } cashReadOnly)
        {
            return cashReadOnly;
        }

        var transferId = Guid.NewGuid();
        metalLeg.TransferId = transferId;

        var leg = new Transaction
        {
            Id = Guid.NewGuid(),
            AssetId = cash.Id,
            Type = isBuy ? TransactionType.Withdraw : TransactionType.Deposit,
            Quantity = metalLeg.Quantity * metalLeg.UnitPriceAmount,
            UnitPriceAmount = 1m,
            Date = metalLeg.Date,
            TransferId = transferId
        };

        var cashQuantity = await dbContext.ReplayCashAsync(cash.Id, leg, cancellationToken);
        if (cashQuantity.IsFailure)
        {
            return cashQuantity.Error;
        }

        return new CashLeg(cash, leg, cashQuantity.Value);
    }

    private sealed record CashLeg(Asset Cash, Transaction Leg, decimal CashQuantity)
    {
        public TransactionTransferResponse ToTransferResponse(Asset metal, Transaction metalLeg) => new()
        {
            TransferId = Leg.TransferId!.Value,
            Manual = TransferLegs.DirectionOf(metalLeg) == TransferDirection.In
                ? TransferRoutes.IsDeletable(Cash.AssetClass, metal.AssetClass)
                : TransferRoutes.IsDeletable(metal.AssetClass, Cash.AssetClass),
            CounterpartAssetId = Cash.Id,
            CounterpartAssetName = Cash.Name,
            Direction = TransferLegs.DirectionOf(metalLeg)
        };
    }
}
