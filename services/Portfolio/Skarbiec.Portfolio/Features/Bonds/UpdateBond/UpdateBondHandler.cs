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
        Guid portfolioId, Guid assetId, UpdateBondRequest request, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookup first: another user's bond, a non-bond asset and a wrong portfolio all end in 404.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Bond, cancellationToken);
        var terms = asset is null
            ? null
            : await dbContext.TreasuryBonds.FirstOrDefaultAsync(t => t.AssetId == assetId, cancellationToken);

        if (asset is null || terms is null)
        {
            return BondErrors.NotFound(assetId);
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

        // A settlement was computed from these terms, so they stay fixed until every settlement is undone.
        if (await dbContext.BondInterestSettlements.AnyAsync(s => s.AssetId == assetId, cancellationToken))
        {
            return BondErrors.Settled;
        }

        var cost = request.BondCount * request.PurchasePricePerBond;

        // A bond holds one opening Deposit, rewritten in place together with its funding leg.
        var opening = await dbContext.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken);
        var fundingLeg = opening.TransferId is { } transferId
            ? await dbContext.Transactions.FirstOrDefaultAsync(t => t.TransferId == transferId && t.Id != opening.Id, cancellationToken)
            : null;
        var rewritesOpening = fundingLeg is null || opening.Quantity != cost || opening.Date != request.PurchaseDate;

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

        if (rewritesOpening)
        {
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

            if (fundingLeg is not null && fundingAsset is not null)
            {
                fundingLeg.Quantity = cost;
                fundingLeg.UnitPriceAmount = 1m;
                fundingLeg.Date = request.PurchaseDate;
                fundingLeg.FxRateToPln = 1m;
                fundingAsset.Quantity = fundingQuantity;
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
        terms.TaxExempt = request.TaxExempt;
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

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, WarsawCalendar.Today(timeProvider), funding, []);
    }
}
