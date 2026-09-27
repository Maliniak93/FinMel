using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;

/// <summary>
/// Pays a settled deposit out later (deposit-payout-to-cash): its whole balance moves to a Cash asset
/// as a Deposit → Cash transfer on the chosen date, emptying the deposit and marking it PaidOut.
/// </summary>
public sealed class PayOutDepositHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, PayOutDepositRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first, as in SettleDeposit: another user's deposit, a non-deposit asset
        // and a deposit under the wrong portfolio all end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Deposit, cancellationToken);
        var terms = asset is null
            ? null
            : await dbContext.TermDeposits.AsNoTracking().FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (asset is null || terms is null)
        {
            return DepositErrors.NotFound(assetId);
        }

        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => p.Id == portfolioId)
            .Select(p => new { p.Name, p.IsArchived })
            .FirstAsync(cancellationToken);

        if (portfolio.IsArchived)
        {
            return PortfolioErrors.Archived(portfolioId);
        }

        if (terms.SettledOn is not { } settledOn)
        {
            return DepositErrors.NotSettled;
        }

        var history = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        // Only a payout writes a Withdraw on a deposit — the same rule the PaidOut status reads.
        if (history.Any(t => t.Type == TransactionType.Withdraw))
        {
            return DepositErrors.AlreadyPaidOut;
        }

        var today = WarsawCalendar.Today(timeProvider);
        if (request.Date < settledOn)
        {
            return DepositErrors.PayoutDateBeforeSettlement;
        }

        if (request.Date > today)
        {
            return DepositErrors.PayoutDateInFuture;
        }

        var planned = await dbContext.PlanPayoutAsync(asset, history, request.DestinationAssetId, request.Date, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error;
        }

        var payout = planned.Value;

        // Last check before the write: both legs freeze the PLN rate of their one date and currency (ADR-026).
        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.Date, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        payout.Stage(dbContext, asset, fxRateToPln.Value);

        // Published before SaveChangesAsync so both outbox rows commit with both legs (ADR-012).
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
        await positionEventPublisher.PublishChangedAsync(payout.Destination, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two racing payouts both bump the deposit row; the loser's xmin no longer matches.
            return TransactionErrors.ConcurrentModification();
        }

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, today, funding, payout.ToInfo());
    }
}
