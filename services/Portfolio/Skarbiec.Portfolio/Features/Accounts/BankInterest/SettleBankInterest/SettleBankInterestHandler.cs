using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest.SettleBankInterest;

public sealed class SettleBankInterestHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<BankInterestSettlementResponse>> HandleAsync(
        Guid accountId, SettleBankInterestRequest request, CancellationToken cancellationToken)
    {
        var found = await dbContext.FindOpenBankAccountAsync(accountId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var (account, money) = found.Value;

        var existingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == money.Id)
            .ToListAsync(cancellationToken);

        var state = await dbContext.LoadBankInterestAsync(account, money, WarsawCalendar.Today(timeProvider), cancellationToken);
        if (state?.Due is not { } due)
        {
            return BankInterestErrors.NotDue;
        }

        if (request.PeriodEnd != due.PeriodEnd)
        {
            return BankInterestErrors.PeriodMismatch(due.PeriodEnd);
        }

        // The net enters as a Deposit, not Interest, whose quantity delta is 0; a zero net still stores the settlement.
        var netInterest = request.GrossInterest - request.Tax;
        var credit = netInterest > 0
            ? new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = money.Id,
                Type = TransactionType.Deposit,
                Quantity = netInterest,
                UnitPriceAmount = 1m,
                Date = due.PeriodEnd
            }
            : null;

        if (credit is not null)
        {
            var recomputed = TransactionQuantityCalculator.Recompute([.. existingTransactions, credit]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            // Last check before the write: the credit freezes the PLN rate of its own date.
            var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(money.Currency, credit.Date, cancellationToken);
            if (fxRateToPln.IsFailure)
            {
                return fxRateToPln.Error;
            }

            credit.FxRateToPln = fxRateToPln.Value;
            dbContext.Transactions.Add(credit);
            money.Quantity = recomputed.Value;
        }

        var settlement = new BankInterestSettlement
        {
            Id = Guid.NewGuid(),
            AssetId = money.Id,
            PeriodStart = due.PeriodStart,
            PeriodEnd = due.PeriodEnd,
            GrossInterest = request.GrossInterest,
            Tax = request.Tax,
            TransactionId = credit?.Id
        };
        dbContext.BankInterestSettlements.Add(settlement);

        // The version bump makes the asset row's xmin guard a racing second settlement.
        await positionEventPublisher.PublishChangedAsync(money, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return settlement.ToResponse();
    }
}
