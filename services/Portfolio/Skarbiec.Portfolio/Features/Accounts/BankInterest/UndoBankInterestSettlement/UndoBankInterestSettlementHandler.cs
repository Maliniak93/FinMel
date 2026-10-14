using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest.UndoBankInterestSettlement;

public sealed class UndoBankInterestSettlementHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid accountId, Guid settlementId, CancellationToken cancellationToken)
    {
        var found = await dbContext.FindOpenBankAccountAsync(accountId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var money = found.Value.Money;

        var settlement = await dbContext.BankInterestSettlements
            .FirstOrDefaultAsync(s => s.Id == settlementId && s.AssetId == money.Id, cancellationToken);

        if (settlement is null)
        {
            return BankInterestErrors.SettlementNotFound(settlementId);
        }

        if (await dbContext.BankInterestSettlements.AnyAsync(
                s => s.AssetId == money.Id && s.PeriodEnd > settlement.PeriodEnd, cancellationToken))
        {
            return BankInterestErrors.SettlementNotLatest;
        }

        if (settlement.TransactionId is { } creditId)
        {
            // Recompute without the credit before removing anything, so a later Withdraw that spent it fails like DeleteTransaction.
            var remainingTransactions = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId == money.Id && t.Id != creditId)
                .ToListAsync(cancellationToken);

            var recomputed = TransactionQuantityCalculator.Recompute(remainingTransactions, money.OversellError(TransactionErrors.MutationBreaksHistory));
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            var credit = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == creditId, cancellationToken);
            if (credit is not null)
            {
                dbContext.Transactions.Remove(credit);
            }

            money.Quantity = recomputed.Value;
        }

        dbContext.BankInterestSettlements.Remove(settlement);

        // Published before SaveChangesAsync so the outbox row commits with the removal.
        await positionEventPublisher.PublishChangedAsync(money, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return Result.Success();
    }
}
