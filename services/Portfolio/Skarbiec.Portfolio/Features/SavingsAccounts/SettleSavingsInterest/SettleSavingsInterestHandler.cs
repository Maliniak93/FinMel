using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.SettleSavingsInterest;

public sealed class SettleSavingsInterestHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<SavingsInterestSettlementResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, SettleSavingsInterestRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's account, a non-Savings asset and a wrong portfolio all end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Savings, cancellationToken);
        var terms = asset is null
            ? null
            : await dbContext.SavingsAccounts.AsNoTracking().FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (asset is null || terms is null)
        {
            return SavingsAccountErrors.NotFound(assetId);
        }

        if (await dbContext.ReadOnlyErrorAsync(asset, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        var existingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        var lastSettledPeriodEnd = await dbContext.SavingsInterestSettlements
            .Where(s => s.AssetId == assetId)
            .MaxAsync(s => (DateOnly?)s.PeriodEnd, cancellationToken);

        var due = SavingsInterestMath.NextDuePeriod(
            [.. existingTransactions.Select(SavingsCashFlow.From)],
            terms.AnnualInterestRatePercent,
            terms.TaxExempt,
            lastSettledPeriodEnd,
            WarsawCalendar.Today(timeProvider));

        if (due is null)
        {
            return SavingsAccountErrors.InterestNotDue;
        }

        if (request.PeriodEnd != due.PeriodEnd)
        {
            return SavingsAccountErrors.InterestPeriodMismatch(due.PeriodEnd);
        }

        // The net enters as a Deposit, not Interest, whose quantity delta is 0; a zero net still stores the settlement.
        var netInterest = request.GrossInterest - request.Tax;
        var credit = netInterest > 0
            ? new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
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
            var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, credit.Date, cancellationToken);
            if (fxRateToPln.IsFailure)
            {
                return fxRateToPln.Error;
            }

            credit.FxRateToPln = fxRateToPln.Value;
            dbContext.Transactions.Add(credit);
            asset.Quantity = recomputed.Value;
        }

        var settlement = new SavingsInterestSettlement
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            PeriodStart = due.PeriodStart,
            PeriodEnd = due.PeriodEnd,
            GrossInterest = request.GrossInterest,
            Tax = request.Tax,
            TransactionId = credit?.Id
        };
        dbContext.SavingsInterestSettlements.Add(settlement);

        // The version bump makes the asset row's xmin guard a racing second settlement.
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

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
