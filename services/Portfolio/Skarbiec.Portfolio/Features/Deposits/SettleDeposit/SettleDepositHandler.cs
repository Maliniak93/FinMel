using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Deposits.SettleDeposit;

public sealed class SettleDepositHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, SettleDepositRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's deposit, a non-deposit asset and a wrong portfolio all end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Deposit, cancellationToken);
        var terms = asset is null
            ? null
            : await dbContext.TermDeposits.FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (asset is null || terms is null)
        {
            return DepositErrors.NotFound(assetId);
        }

        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => p.Id == portfolioId)
            .Select(p => new { p.Name, p.IsArchived })
            .FirstAsync(cancellationToken);

        if (asset.ReadOnlyError(portfolio.IsArchived) is { } readOnly)
        {
            return readOnly;
        }

        if (terms.SettledOn is not null)
        {
            return DepositErrors.AlreadySettled;
        }

        var today = WarsawCalendar.Today(timeProvider);
        if (terms.MaturityDate > today)
        {
            return DepositErrors.NotDue;
        }

        if (request.SettledOn < terms.StartDate)
        {
            return DepositErrors.SettledOnBeforeStart;
        }

        if (request.SettledOn > today)
        {
            return DepositErrors.SettledOnInFuture;
        }

        // The net enters as a Deposit, not Interest, whose quantity delta is 0; nothing to credit when it is 0.
        var netInterest = request.GrossInterest - request.Tax;
        var credit = netInterest > 0
            ? new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                Type = TransactionType.Deposit,
                Quantity = netInterest,
                UnitPriceAmount = 1m,
                Date = request.SettledOn
            }
            : null;

        // Recompute over the full history: the quantity comes out as principal + net.
        var existingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        List<Transaction> history = credit is null ? existingTransactions : [.. existingTransactions, credit];
        var recomputed = TransactionQuantityCalculator.Recompute(history);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // Checked before anything is staged, so an invalid destination leaves the deposit unsettled.
        PayoutTransfer? payout = null;
        if (request.DestinationAssetId is { } destinationAssetId)
        {
            var planned = await dbContext.PlanPayoutAsync(asset, history, destinationAssetId, request.SettledOn, cancellationToken);
            if (planned.IsFailure)
            {
                return planned.Error;
            }

            payout = planned.Value;
        }

        decimal? fxRateToPln = null;
        if (credit is not null || payout is not null)
        {
            // Last check before the write: the credit and both legs freeze the PLN rate of their one date and currency.
            var resolved = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.SettledOn, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            fxRateToPln = resolved.Value;
        }

        if (credit is not null)
        {
            credit.FxRateToPln = fxRateToPln;
            dbContext.Transactions.Add(credit);
        }

        asset.Quantity = recomputed.Value;

        // Empties the deposit: its quantity becomes 0 and the destination's rises by the whole balance.
        payout?.Stage(dbContext, asset, fxRateToPln);

        terms.SettledOn = request.SettledOn;
        terms.SettledGrossInterest = request.GrossInterest;
        terms.SettledTax = request.Tax;

        // One outbox row per asset, each with its final quantity, committed with the settlement.
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        if (payout is not null)
        {
            await positionEventPublisher.PublishChangedAsync(payout.Destination, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two racing settlements both bump the asset row; the loser's xmin is stale.
            return TransactionErrors.ConcurrentModification();
        }

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, today, funding, payout?.ToInfo());
    }
}
