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
            return TransferErrors.LegManaged;
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
}
