using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds.UpdateBond;

public sealed class UpdateBondHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    TimeProvider timeProvider)
{
    public async Task<Result<BondResponse>> HandleAsync(
        Guid assetId, UpdateBondRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's bond, a non-bond asset all end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.AssetClass == AssetClass.Bond, cancellationToken);
        var terms = asset is null
            ? null
            : await dbContext.TreasuryBonds.FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (asset is null || terms is null)
        {
            return BondErrors.NotFound(assetId);
        }

        if (await dbContext.ReadOnlyErrorAsync(asset, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        if (await dbContext.BondRedemptions.AnyAsync(r => r.AssetId == assetId, cancellationToken))
        {
            return BondErrors.Redeemed;
        }

        // A settlement was computed from these terms, so they stay fixed until every settlement is undone.
        if (await dbContext.BondInterestSettlements.AnyAsync(s => s.AssetId == assetId, cancellationToken))
        {
            return BondErrors.Settled;
        }

        // A swap-born bond's purchase was paid by the old bond's redemption, so only what that transfer did not fix can change.
        if (terms.SwappedFromAssetId is not null
            && (request.BondCount != terms.BondCount
                || request.PurchasePricePerBond != terms.PurchasePricePerBond
                || request.PurchaseDate != terms.PurchaseDate
                || request.SeriesCode != terms.SeriesCode
                || request.Type != terms.Type))
        {
            return BondErrors.FromSwap;
        }

        var cost = request.BondCount * request.PurchasePricePerBond;

        // A swap-born bond's opening legs are fixed by the swap; a detached opening (source bond removed) has no cash leg and is rewritten alone.
        Asset? fundingAsset = null;
        var openings = terms.SwappedFromAssetId is null
            ? await dbContext.Transactions.Where(t => t.AssetId == assetId).ToListAsync(cancellationToken)
            : [];
        if (openings is [var opening] && (opening.Quantity != cost || opening.Date != request.PurchaseDate))
        {
            var fundingLeg = opening.TransferId is { } transferId
                ? await dbContext.Transactions.FirstOrDefaultAsync(
                    t => t.TransferId == transferId && t.Id != opening.Id, cancellationToken)
                : null;

            decimal? fundingQuantity = null;
            if (fundingLeg is not null)
            {
                fundingAsset = await dbContext.Assets.FirstAsync(a => a.Id == fundingLeg.AssetId, cancellationToken);

                // The rewritten Out leg must be covered everywhere in the cash history, not just at the end.
                var fundingHistory = await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.AssetId == fundingAsset.Id && t.Id != fundingLeg.Id)
                    .ToListAsync(cancellationToken);
                var fundingCandidate = new Transaction
                {
                    Id = fundingLeg.Id,
                    AssetId = fundingAsset.Id,
                    Type = fundingLeg.Type,
                    Quantity = cost,
                    UnitPriceAmount = 1m,
                    Date = request.PurchaseDate
                };

                var fundingRecomputed = TransactionQuantityCalculator.Recompute(
                    [.. fundingHistory, fundingCandidate], _ => TransferErrors.InsufficientFunds);
                if (fundingRecomputed.IsFailure)
                {
                    return fundingRecomputed.Error;
                }

                fundingQuantity = fundingRecomputed.Value;
            }

            var candidate = new Transaction
            {
                Id = opening.Id,
                AssetId = assetId,
                Type = TransactionType.Deposit,
                Quantity = cost,
                UnitPriceAmount = 1m,
                Date = request.PurchaseDate
            };

            var recomputed = TransactionQuantityCalculator.Recompute([candidate]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            opening.Quantity = candidate.Quantity;
            opening.UnitPriceAmount = candidate.UnitPriceAmount;
            opening.Date = candidate.Date;
            opening.FxRateToPln = 1m;
            asset.Quantity = recomputed.Value;

            if (fundingLeg is not null && fundingAsset is not null && fundingQuantity is { } quantity)
            {
                fundingLeg.Quantity = cost;
                fundingLeg.UnitPriceAmount = 1m;
                fundingLeg.Date = request.PurchaseDate;
                fundingLeg.FxRateToPln = 1m;
                fundingAsset.Quantity = quantity;
            }
        }

        asset.Name = request.Name;

        terms.SeriesCode = request.SeriesCode;
        terms.Type = request.Type;
        terms.PurchaseDate = request.PurchaseDate;
        terms.BondCount = request.BondCount;
        terms.PurchasePricePerBond = request.PurchasePricePerBond;
        terms.FirstPeriodRatePercent = request.FirstPeriodRatePercent;
        terms.MarginPercent = request.MarginPercent;
        terms.EarlyRedemptionFeePerBond = request.EarlyRedemptionFeePerBond;
        terms.MaturityDate = BondSchedule.MaturityDate(request.Type, request.PurchaseDate);

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

        return (await dbContext.LoadBondAsync(assetId, WarsawCalendar.Today(timeProvider), cancellationToken))!;
    }
}
