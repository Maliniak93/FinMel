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
        Guid assetId, UpdateDepositRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's deposit, a non-deposit asset both end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.AssetClass == AssetClass.Deposit, cancellationToken);
        var terms = asset is null
            ? null
            : await dbContext.TermDeposits.FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (asset is null || terms is null)
        {
            return DepositErrors.NotFound(assetId);
        }

        if (await dbContext.ReadOnlyErrorAsync(asset, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        // A settled deposit's terms are immutable; only its return account may still move, until it is paid out.
        if (terms.SettledOn is not null)
        {
            return await ChangeSettledReturnAccountAsync(asset, terms, request, cancellationToken);
        }

        // A rolled-over deposit's principal and start date came from earlier terms, so they are fixed.
        if (terms.RolloverCount > 0 && (request.Principal != terms.Principal || request.StartDate != terms.StartDate))
        {
            return DepositErrors.RolledOver;
        }

        // The return account may change until the deposit is paid out; the source never does.
        var returnAccountId = request.ReturnAccountId ?? terms.ReturnAccountId;
        if (returnAccountId != terms.ReturnAccountId)
        {
            var resolved = await dbContext.ResolveReturnAsync(returnAccountId, asset.Currency, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }
        }

        // An unsettled, never rolled-over deposit holds one opening Deposit, rewritten in place.
        var opening = terms.RolloverCount == 0
            ? await dbContext.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken)
            : null;

        // The deposit's legs move together with the bank account's, and only when the principal or the start date changes.
        var bankLeg = opening?.TransferId is { } transferId
            ? await dbContext.Transactions.FirstOrDefaultAsync(t => t.TransferId == transferId && t.Id != opening.Id, cancellationToken)
            : null;
        var rewritesOpening = opening is not null
            && (bankLeg is null || opening.Quantity != request.Principal || opening.Date != request.StartDate);

        Asset? bankAsset = null;
        var bankQuantity = 0m;
        if (bankLeg is not null && rewritesOpening)
        {
            bankAsset = await dbContext.Assets.FirstAsync(a => a.Id == bankLeg.AssetId, cancellationToken);

            // The bank account is read-only while it is archived, so its leg cannot be rewritten.
            if (await dbContext.ReadOnlyErrorAsync(bankAsset, cancellationToken) is { } bankReadOnly)
            {
                return bankReadOnly;
            }

            // The rewritten Out leg must be covered everywhere in the bank account's history, not just at the end.
            var bankHistory = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId == bankAsset.Id && t.Id != bankLeg.Id)
                .ToListAsync(cancellationToken);
            var bankCandidate = new Transaction
            {
                Id = bankLeg.Id,
                AssetId = bankAsset.Id,
                Type = bankLeg.Type,
                Quantity = request.Principal,
                UnitPriceAmount = 1m,
                Date = request.StartDate
            };

            var bankRecomputed = TransactionQuantityCalculator.Recompute(
                [.. bankHistory, bankCandidate], _ => TransferErrors.InsufficientFunds);
            if (bankRecomputed.IsFailure)
            {
                return bankRecomputed.Error;
            }

            bankQuantity = bankRecomputed.Value;
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

            if (bankLeg is not null && bankAsset is not null)
            {
                bankLeg.Quantity = request.Principal;
                bankLeg.UnitPriceAmount = 1m;
                bankLeg.Date = request.StartDate;
                bankLeg.FxRateToPln = fxRateToPln.Value;
                bankAsset.Quantity = bankQuantity;
            }
        }

        asset.Name = request.Name;

        terms.BankName = request.BankName;
        terms.ReturnAccountId = returnAccountId;
        terms.Principal = request.Principal;
        terms.StartDate = request.StartDate;
        terms.TermLength = request.TermLength;
        terms.TermUnit = request.TermUnit;
        terms.MaturityDate = DepositInterestMath.MaturityDate(request.StartDate, request.TermLength, request.TermUnit);
        terms.AnnualInterestRatePercent = request.AnnualInterestRatePercent;
        terms.Capitalization = request.Capitalization;
        terms.EarlyBreakInterestLossPercent = request.EarlyBreakInterestLossPercent;

        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        // Both assets publish in the same save as both legs.
        if (bankAsset is not null)
        {
            await positionEventPublisher.PublishChangedAsync(bankAsset, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        var accountNames = await dbContext.LoadAccountNamesAsync([terms], cancellationToken);

        // Never paid out: only a settled deposit can be, and its terms are immutable.
        return terms.ToResponse(asset, WarsawCalendar.Today(timeProvider), accountNames, paidOutOn: null);
    }

    private async Task<Result<DepositResponse>> ChangeSettledReturnAccountAsync(
        Asset asset, TermDeposit terms, UpdateDepositRequest request, CancellationToken cancellationToken)
    {
        var termsUnchanged = request.Name == asset.Name
            && request.BankName == terms.BankName
            && request.Principal == terms.Principal
            && request.StartDate == terms.StartDate
            && request.TermLength == terms.TermLength
            && request.TermUnit == terms.TermUnit
            && request.AnnualInterestRatePercent == terms.AnnualInterestRatePercent
            && request.Capitalization == terms.Capitalization
            && request.EarlyBreakInterestLossPercent == terms.EarlyBreakInterestLossPercent;
        if (!termsUnchanged || request.ReturnAccountId is not { } returnAccountId || returnAccountId == terms.ReturnAccountId)
        {
            return DepositErrors.Settled;
        }

        var paidOutOn = await dbContext.LoadPayoutDateAsync(asset.Id, cancellationToken);
        if (paidOutOn is not null)
        {
            return DepositErrors.Settled;
        }

        var resolved = await dbContext.ResolveReturnAsync(returnAccountId, asset.Currency, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        terms.ReturnAccountId = returnAccountId;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        var accountNames = await dbContext.LoadAccountNamesAsync([terms], cancellationToken);
        return terms.ToResponse(asset, WarsawCalendar.Today(timeProvider), accountNames, paidOutOn: null);
    }
}
