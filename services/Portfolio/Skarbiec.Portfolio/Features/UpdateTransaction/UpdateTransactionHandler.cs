using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.UpdateTransaction;

public sealed class UpdateTransactionHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, IFxRateLookupClient fxRateLookupClient)
{
    public async Task<Result<TransactionResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, Guid id, UpdateTransactionRequest request, CancellationToken cancellationToken)
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

        // A term deposit's only transaction is its opening one, rewritten by UpdateDeposit.
        if (asset.AssetClass == AssetClass.Deposit)
        {
            return DepositErrors.TransactionsManaged;
        }

        // A treasury bond's only transaction is its opening one, rewritten by UpdateBond.
        if (asset.AssetClass == AssetClass.Bond)
        {
            return BondErrors.TransactionsManaged;
        }

        var transaction = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.AssetId == assetId, cancellationToken);

        if (transaction is null)
        {
            return TransactionErrors.NotFound(id);
        }

        // A transfer leg changes only through its transfer's entry point, so the two legs never drift apart.
        if (transaction.TransferId is not null)
        {
            // A stock or ETF trade's pair is edited from the security side.
            return asset.AssetClass is AssetClass.Stock or AssetClass.Etf
                ? await UpdateTradeAsync(asset, transaction, request, cancellationToken)
                : TransferErrors.LegManaged;
        }

        // A savings interest credit goes only by undoing its settlement, so a settlement never loses its credit.
        if (asset.AssetClass == AssetClass.Savings
            && await dbContext.SavingsInterestSettlements.AnyAsync(s => s.TransactionId == id, cancellationToken))
        {
            return SavingsAccountErrors.InterestManaged;
        }

        if (!AssetTransactionTypes.IsAllowed(asset.AssetClass, request.Type))
        {
            return TransactionErrors.TypeNotAllowedForClass(request.Type, asset.AssetClass);
        }

        var unitPrice = Money.Create(request.UnitPrice, asset.Currency);
        if (unitPrice.IsFailure)
        {
            return unitPrice.Error;
        }

        // Recompute with the edited candidate before touching the tracked transaction, so a rejected edit leaves the database untouched.
        var otherTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId && t.Id != id)
            .ToListAsync(cancellationToken);

        var candidate = new Transaction
        {
            Id = transaction.Id,
            AssetId = assetId,
            Type = request.Type,
            Quantity = request.Quantity,
            UnitPriceAmount = unitPrice.Value.Amount,
            Date = request.Date
        };

        var recomputed = TransactionQuantityCalculator.Recompute([.. otherTransactions, candidate], TransactionErrors.MutationBreaksHistory);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // Re-resolved on every update so the stored rate always matches the current date.
        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.Date, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        transaction.Type = candidate.Type;
        transaction.Quantity = candidate.Quantity;
        transaction.UnitPriceAmount = candidate.UnitPriceAmount;
        transaction.FxRateToPln = fxRateToPln.Value;
        transaction.Date = candidate.Date;
        asset.Quantity = recomputed.Value;

        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return transaction.ToResponse(asset.Currency);
    }

    private async Task<Result<TransactionResponse>> UpdateTradeAsync(
        Asset asset, Transaction transaction, UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        if (request.Type != transaction.Type)
        {
            return TransferErrors.LegManaged;
        }

        var cashLeg = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.TransferId == transaction.TransferId && t.Id != transaction.Id, cancellationToken);
        if (cashLeg is null)
        {
            return TransferErrors.LegManaged;
        }

        var cash = await dbContext.Assets.FirstAsync(a => a.Id == cashLeg.AssetId, cancellationToken);
        if (await dbContext.ReadOnlyErrorAsync(cash, cancellationToken) is { } cashReadOnly)
        {
            return cashReadOnly;
        }

        var unitPrice = Money.Create(request.UnitPrice, asset.Currency);
        if (unitPrice.IsFailure)
        {
            return unitPrice.Error;
        }

        var securityUnitPrice = TransferLegs.SecurityUnitPrice(request.Type, unitPrice.Value.Amount);
        var candidate = new Transaction
        {
            Id = transaction.Id,
            AssetId = asset.Id,
            Type = request.Type,
            Quantity = request.Quantity,
            UnitPriceAmount = securityUnitPrice,
            Date = request.Date
        };

        var cashAmount = TransferLegs.TradeCashAmount(request.Type, request.Quantity, securityUnitPrice);
        var cashCandidate = new Transaction
        {
            Id = cashLeg.Id,
            AssetId = cash.Id,
            Type = cashLeg.Type,
            Quantity = cashAmount,
            UnitPriceAmount = 1m,
            Date = request.Date
        };

        var securityQuantity = await RecomputeWithAsync(asset.Id, candidate, TransactionErrors.MutationBreaksHistory, cancellationToken);
        if (securityQuantity.IsFailure)
        {
            return securityQuantity.Error;
        }

        var cashQuantity = await RecomputeWithAsync(cash.Id, cashCandidate, _ => TransferErrors.InsufficientFunds, cancellationToken);
        if (cashQuantity.IsFailure)
        {
            return cashQuantity.Error;
        }

        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.Date, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        transaction.Quantity = candidate.Quantity;
        transaction.UnitPriceAmount = candidate.UnitPriceAmount;
        transaction.FxRateToPln = fxRateToPln.Value;
        transaction.Date = candidate.Date;
        cashLeg.Quantity = cashAmount;
        cashLeg.FxRateToPln = fxRateToPln.Value;
        cashLeg.Date = request.Date;
        asset.Quantity = securityQuantity.Value;
        cash.Quantity = cashQuantity.Value;

        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
        await positionEventPublisher.PublishChangedAsync(cash, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return transaction.ToResponse(asset.Currency);
    }

    private async Task<Result<decimal>> RecomputeWithAsync(
        Guid assetId, Transaction candidate, Func<TransactionType, Error> oversellError, CancellationToken cancellationToken)
    {
        var others = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId && t.Id != candidate.Id)
            .ToListAsync(cancellationToken);

        return TransactionQuantityCalculator.Recompute([.. others, candidate], oversellError);
    }
}
