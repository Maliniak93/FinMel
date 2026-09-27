using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;

/// <summary>
/// Rolls a matured deposit over into its next term on the same asset (deposit-rollover). A Due deposit
/// is settled in the same save — its net-interest credit dated on the maturity date, as the bank pays
/// it then — and a Settled one (not paid out) reuses its stored settlement. Either way the whole
/// balance becomes the new principal, the old maturity date the new start date, and only the rate
/// changes; the settlement fields are cleared and <see cref="TermDeposit.RolloverCount"/> goes up.
/// </summary>
public sealed class RollOverDepositHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, RollOverDepositRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first, as in SettleDeposit: another user's deposit, a non-deposit asset
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

        // The bank renews on the maturity day: the credit of a Due deposit is dated then, and the next
        // term always starts then, however late the rollover happens.
        var endedOn = terms.MaturityDate;
        var today = WarsawCalendar.Today(timeProvider);
        decimal netInterest;
        var settlesNow = terms.SettledOn is null;

        if (!settlesNow)
        {
            // Only a payout writes a Withdraw on a deposit — the same rule the PaidOut status reads.
            if (await dbContext.Transactions.AnyAsync(
                    t => t.AssetId == assetId && t.Type == TransactionType.Withdraw, cancellationToken))
            {
                return DepositErrors.AlreadyPaidOut;
            }

            if (request.GrossInterest is not null || request.Tax is not null)
            {
                return DepositErrors.AlreadySettled;
            }

            // The settlement's net is already in the deposit as its credit: no transaction, no event.
            netInterest = terms.SettledGrossInterest.GetValueOrDefault() - terms.SettledTax.GetValueOrDefault();
        }
        else
        {
            if (terms.MaturityDate > today)
            {
                return DepositErrors.NotDue;
            }

            if (request.GrossInterest is not { } grossInterest || request.Tax is not { } tax)
            {
                return DepositErrors.SettlementAmountsRequired;
            }

            netInterest = grossInterest - tax;

            // As in SettleDeposit: the net interest enters as a Deposit transaction (ADR-009), none when 0.
            var credit = netInterest > 0
                ? new Transaction
                {
                    Id = Guid.NewGuid(),
                    AssetId = assetId,
                    Type = TransactionType.Deposit,
                    Quantity = netInterest,
                    UnitPriceAmount = 1m,
                    Date = endedOn
                }
                : null;

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

            if (credit is not null)
            {
                // Last check before the write: the credit freezes the PLN rate of its date (ADR-026).
                var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, endedOn, cancellationToken);
                if (fxRateToPln.IsFailure)
                {
                    return fxRateToPln.Error;
                }

                credit.FxRateToPln = fxRateToPln.Value;
                dbContext.Transactions.Add(credit);
            }

            asset.Quantity = recomputed.Value;
        }

        // The next term: the whole balance, from the old maturity date, on the same terms but the rate.
        terms.Principal += netInterest;
        terms.StartDate = endedOn;
        terms.MaturityDate = DepositInterestMath.MaturityDate(endedOn, terms.TermLength, terms.TermUnit);
        terms.AnnualInterestRatePercent = request.AnnualInterestRatePercent;
        terms.SettledOn = null;
        terms.SettledGrossInterest = null;
        terms.SettledTax = null;
        terms.RolloverCount++;

        // Published before SaveChangesAsync so the outbox row commits with the rollover (ADR-012) — only
        // when settling now, as the credit moves the quantity; a Settled deposit's quantity stays.
        if (settlesNow)
        {
            await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A racing settlement or rollover bumped the asset row first; the loser's xmin no longer matches.
            return TransactionErrors.ConcurrentModification();
        }

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);

        // Never paid out: a paid-out deposit is rejected above.
        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, today, funding, payout: null);
    }
}
