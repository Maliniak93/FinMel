using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;

public sealed class UpdateDepositHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, UpdateDepositRequest request, CancellationToken cancellationToken)
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

        // A settled deposit's terms are immutable; delete it instead.
        if (terms.SettledOn is not null)
        {
            return DepositErrors.Settled;
        }

        // A rolled-over deposit's principal and start date came from earlier terms, so they are fixed.
        if (terms.RolloverCount > 0 && (request.Principal != terms.Principal || request.StartDate != terms.StartDate))
        {
            return DepositErrors.RolledOver;
        }

        // An unsettled, never rolled-over deposit holds one opening Deposit, rewritten in place.
        var opening = terms.RolloverCount == 0
            ? await dbContext.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken)
            : null;

        // A funded deposit's legs move together, and only when the principal or the start date changes.
        var fundingLeg = opening?.TransferId is { } transferId
            ? await dbContext.Transactions.FirstOrDefaultAsync(t => t.TransferId == transferId && t.Id != opening.Id, cancellationToken)
            : null;
        var rewritesOpening = opening is not null
            && (fundingLeg is null || opening.Quantity != request.Principal || opening.Date != request.StartDate);

        Asset? fundingAsset = null;
        var fundingQuantity = 0m;
        if (fundingLeg is not null && rewritesOpening)
        {
            fundingAsset = await dbContext.Assets.FirstAsync(a => a.Id == fundingLeg.AssetId, cancellationToken);

            // The Cash side is read-only while it or its portfolio is archived, so its leg cannot be rewritten.
            if (await dbContext.ReadOnlyErrorAsync(fundingAsset, cancellationToken) is { } fundingReadOnly)
            {
                return fundingReadOnly;
            }

            // The rewritten Out leg must be covered everywhere in the Cash history, not just at the end.
            var fundingHistory = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId == fundingAsset.Id && t.Id != fundingLeg.Id)
                .ToListAsync(cancellationToken);
            var fundingCandidate = new Transaction
            {
                Id = fundingLeg.Id,
                AssetId = fundingAsset.Id,
                Type = fundingLeg.Type,
                Quantity = request.Principal,
                UnitPriceAmount = 1m,
                Date = request.StartDate
            };

            var fundingRecomputed = TransactionQuantityCalculator.Recompute(
                [.. fundingHistory, fundingCandidate], _ => TransferErrors.InsufficientFunds);
            if (fundingRecomputed.IsFailure)
            {
                return fundingRecomputed.Error;
            }

            fundingQuantity = fundingRecomputed.Value;
        }

        if (rewritesOpening && opening is not null)
        {
            var candidate = new Transaction
            {
                Id = opening.Id,
                AssetId = assetId,
                Type = TransactionType.Deposit,
                Quantity = request.Principal,
                UnitPriceAmount = 1m,
                Date = request.StartDate
            };

            var recomputed = TransactionQuantityCalculator.Recompute([candidate]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            // Re-resolved on every rewrite so the stored rate matches the current start date; both legs share it.
            var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.StartDate, cancellationToken);
            if (fxRateToPln.IsFailure)
            {
                return fxRateToPln.Error;
            }

            opening.Quantity = candidate.Quantity;
            opening.UnitPriceAmount = candidate.UnitPriceAmount;
            opening.Date = candidate.Date;
            opening.FxRateToPln = fxRateToPln.Value;
            asset.Quantity = recomputed.Value;

            if (fundingLeg is not null && fundingAsset is not null)
            {
                fundingLeg.Quantity = request.Principal;
                fundingLeg.UnitPriceAmount = 1m;
                fundingLeg.Date = request.StartDate;
                fundingLeg.FxRateToPln = fxRateToPln.Value;
                fundingAsset.Quantity = fundingQuantity;
            }
        }

        asset.Name = request.Name;

        terms.BankName = request.BankName;
        terms.Principal = request.Principal;
        terms.StartDate = request.StartDate;
        terms.TermLength = request.TermLength;
        terms.TermUnit = request.TermUnit;
        terms.MaturityDate = DepositInterestMath.MaturityDate(request.StartDate, request.TermLength, request.TermUnit);
        terms.AnnualInterestRatePercent = request.AnnualInterestRatePercent;
        terms.Capitalization = request.Capitalization;
        terms.TaxExempt = request.TaxExempt;
        terms.EarlyBreakInterestLossPercent = request.EarlyBreakInterestLossPercent;

        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        // Both assets publish in the same save as both legs.
        if (fundingAsset is not null)
        {
            await positionEventPublisher.PublishChangedAsync(fundingAsset, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);

        // Never paid out: only a settled deposit can be, and its terms are immutable.
        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, WarsawCalendar.Today(timeProvider), funding, payout: null);
    }
}
