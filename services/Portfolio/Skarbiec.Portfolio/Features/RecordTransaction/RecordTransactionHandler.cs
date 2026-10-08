using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.RecordTransaction;

public sealed class RecordTransactionHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, IFxRateLookupClient fxRateLookupClient)
{
    public async Task<Result<TransactionResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, RecordTransactionRequest request, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

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

        if (request.CashAssetId is not null && !MovesMoneyThroughCash(asset.AssetClass, request.Type))
        {
            return TransferErrors.CashLinkNotAllowed;
        }

        var unitPrice = Money.Create(request.UnitPrice, asset.Currency);
        if (unitPrice.IsFailure)
        {
            return unitPrice.Error;
        }

        Transaction transaction;
        Transaction? securityCashLeg = null;
        if (request.CashAssetId is { } linkedCashAssetId && asset.AssetClass != AssetClass.PreciousMetal)
        {
            (transaction, securityCashLeg) = TransferLegs.CreateTrade(
                assetId, linkedCashAssetId, request.Type, request.Quantity, unitPrice.Value.Amount, request.Date);
        }
        else
        {
            transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                Type = request.Type,
                Quantity = request.Quantity,
                UnitPriceAmount = unitPrice.Value.Amount,
                Date = request.Date
            };
        }

        // Recompute over the full history rather than incrementing in place: the one path shared with edit and delete.
        var existingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        var recomputed = TransactionQuantityCalculator.Recompute([.. existingTransactions, transaction]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        CashLeg? cashLeg = null;
        if (request.CashAssetId is { } cashAssetId)
        {
            var planned = await PlanCashLegAsync(cashAssetId, asset, transaction, securityCashLeg, cancellationToken);
            if (planned.IsFailure)
            {
                return planned.Error;
            }

            cashLeg = planned.Value;
        }

        // Last check before the write: only a valid request on an active portfolio asks MarketData.
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

        asset.Quantity = recomputed.Value;
        dbContext.Transactions.Add(transaction);

        // The event carries the recomputed quantity, so no consumer has to replay the history.
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

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

        return transaction.ToResponse(asset.Currency, cashLeg?.ToTransferResponse(asset, transaction));
    }

    private static bool MovesMoneyThroughCash(AssetClass assetClass, TransactionType type) => assetClass switch
    {
        AssetClass.PreciousMetal => type is TransactionType.Buy or TransactionType.Sell,
        AssetClass.Stock or AssetClass.Etf => type is TransactionType.Buy or TransactionType.Sell or TransactionType.Dividend,
        _ => false
    };

    private async Task<Result<CashLeg>> PlanCashLegAsync(
        Guid cashAssetId, Asset holding, Transaction holdingLeg, Transaction? plannedLeg, CancellationToken cancellationToken)
    {
        // Through the tenancy filter: a stranger's Cash gets the same 400 as any unsuitable counterpart.
        var cash = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == cashAssetId, cancellationToken);
        var isBuy = holdingLeg.Type == TransactionType.Buy;

        if (cash is null
            || cash.AssetClass != AssetClass.Cash
            || !(isBuy ? TransferRoutes.IsAllowed(cash.AssetClass, holding.AssetClass) : TransferRoutes.IsAllowed(holding.AssetClass, cash.AssetClass))
            || cash.Currency != holding.Currency
            || cash.IsArchived
            || await dbContext.IsPortfolioArchivedAsync(cash.PortfolioId, cancellationToken))
        {
            return TransferErrors.InvalidCounterpart;
        }

        var leg = plannedLeg;
        if (leg is null)
        {
            var transferId = Guid.NewGuid();
            holdingLeg.TransferId = transferId;

            leg = new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = cash.Id,
                Type = isBuy ? TransactionType.Withdraw : TransactionType.Deposit,
                Quantity = holdingLeg.Quantity * holdingLeg.UnitPriceAmount,
                UnitPriceAmount = 1m,
                Date = holdingLeg.Date,
                TransferId = transferId
            };
        }

        var cashHistory = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == cash.Id)
            .ToListAsync(cancellationToken);

        var cashQuantity = TransactionQuantityCalculator.Recompute(
            [.. cashHistory, leg], _ => TransferErrors.InsufficientFunds);
        if (cashQuantity.IsFailure)
        {
            return cashQuantity.Error;
        }

        var cashPortfolioName = await dbContext.Portfolios
            .Where(p => p.Id == cash.PortfolioId)
            .Select(p => p.Name)
            .FirstAsync(cancellationToken);

        return new CashLeg(cash, cashPortfolioName, leg, cashQuantity.Value);
    }

    private sealed record CashLeg(Asset Cash, string CashPortfolioName, Transaction Leg, decimal CashQuantity)
    {
        public TransactionTransferResponse ToTransferResponse(Asset metal, Transaction metalLeg) => new()
        {
            TransferId = Leg.TransferId!.Value,
            Manual = TransferLegs.DirectionOf(metalLeg) == TransferDirection.In
                ? TransferRoutes.IsDeletable(Cash.AssetClass, metal.AssetClass)
                : TransferRoutes.IsDeletable(metal.AssetClass, Cash.AssetClass),
            CounterpartAssetId = Cash.Id,
            CounterpartAssetName = Cash.Name,
            CounterpartPortfolioId = Cash.PortfolioId,
            CounterpartPortfolioName = CashPortfolioName,
            Direction = TransferLegs.DirectionOf(metalLeg)
        };
    }
}
