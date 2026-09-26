using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Deposits.SettleDeposit;

/// <summary>
/// Settles a Due term deposit (term-deposits-settlement): stores what the bank actually paid and
/// credits the net interest to the deposit itself — the money stays in it until a payout moves it.
/// </summary>
public sealed class SettleDepositHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, SettleDepositRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first, as in UpdateDeposit: another user's deposit, a non-deposit asset
        // and a deposit under the wrong portfolio all end in 404.
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

        if (portfolio.IsArchived)
        {
            return PortfolioErrors.Archived(portfolioId);
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

        // The net interest enters as a Deposit transaction, not Interest: Interest has a quantity delta
        // of 0, and the deposit's value must rise by it (ADR-009). Nothing to credit when it is 0.
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

        // Recompute over the full history (the opening transaction + the credit), the single code path
        // deriving Asset.Quantity: it comes out as principal + net.
        var existingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        var recomputed = TransactionQuantityCalculator.Recompute(
            credit is null ? existingTransactions : [.. existingTransactions, credit]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        if (credit is not null)
        {
            // Last check before the write: the credit freezes the PLN rate of its own date (ADR-026).
            var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.SettledOn, cancellationToken);
            if (fxRateToPln.IsFailure)
            {
                return fxRateToPln.Error;
            }

            credit.FxRateToPln = fxRateToPln.Value;
            dbContext.Transactions.Add(credit);
        }

        asset.Quantity = recomputed.Value;
        terms.SettledOn = request.SettledOn;
        terms.SettledGrossInterest = request.GrossInterest;
        terms.SettledTax = request.Tax;

        // Published before SaveChangesAsync so the outbox row commits with the settlement (ADR-012).
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two racing settlements both bump the asset row; the loser's xmin no longer matches.
            return TransactionErrors.ConcurrentModification();
        }

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, today, funding);
    }
}
