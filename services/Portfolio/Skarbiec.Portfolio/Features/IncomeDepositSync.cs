using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features;

internal sealed record IncomeDepositSyncResult(List<Asset> Touched, Guid? TransactionId);

internal static class IncomeDepositSync
{
    // Stages the deposit's create, update, move or removal and returns the money assets whose position changed.
    public static async Task<Result<IncomeDepositSyncResult>> SyncIncomeDepositAsync(
        this PortfolioDbContext dbContext,
        Guid? existingTransactionId,
        Guid accountId,
        bool createDeposit,
        decimal amount,
        DateOnly date,
        decimal fxRateToPln,
        CancellationToken cancellationToken)
    {
        var existing = existingTransactionId is { } existingId
            ? await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == existingId, cancellationToken)
            : null;
        var oldMoney = existing is null
            ? null
            : await dbContext.Assets.FirstAsync(a => a.Id == existing.AssetId, cancellationToken);
        var newMoney = createDeposit ? await dbContext.LoadAccountCashAsync(accountId, cancellationToken) : null;

        List<Asset> touched = [];
        if (oldMoney is null && newMoney is null)
        {
            return new IncomeDepositSyncResult(touched, null);
        }

        foreach (var money in new[] { oldMoney, newMoney })
        {
            if (money is not null && await dbContext.ReadOnlyErrorAsync(money, cancellationToken) is { } readOnly)
            {
                return readOnly;
            }
        }

        var candidate = newMoney is null
            ? null
            : new Transaction
            {
                Id = existing is not null && existing.AssetId == newMoney.Id ? existing.Id : Guid.NewGuid(),
                AssetId = newMoney.Id,
                Type = TransactionType.Deposit,
                Quantity = amount,
                UnitPriceAmount = 1m,
                FxRateToPln = fxRateToPln,
                Date = date
            };

        foreach (var money in new[] { oldMoney, newMoney }.OfType<Asset>().DistinctBy(a => a.Id))
        {
            var others = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId == money.Id && (existing == null || t.Id != existing.Id))
                .ToListAsync(cancellationToken);
            if (candidate?.AssetId == money.Id)
            {
                others.Add(candidate);
            }

            var recomputed = TransactionQuantityCalculator.Recompute(others, money.OversellError(TransactionErrors.MutationBreaksHistory));
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            money.Quantity = recomputed.Value;
            touched.Add(money);
        }

        if (existing is not null && candidate?.Id == existing.Id)
        {
            existing.Quantity = candidate.Quantity;
            existing.Date = candidate.Date;
            existing.FxRateToPln = candidate.FxRateToPln;
        }
        else
        {
            if (existing is not null)
            {
                dbContext.Transactions.Remove(existing);
            }

            if (candidate is not null)
            {
                dbContext.Transactions.Add(candidate);
            }
        }

        return new IncomeDepositSyncResult(touched, candidate?.Id);
    }
}
