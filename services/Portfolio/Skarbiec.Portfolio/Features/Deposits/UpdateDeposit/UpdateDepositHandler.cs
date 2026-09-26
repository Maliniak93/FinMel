using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
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
        // Tenancy-scoped lookup first: another user's deposit, a non-deposit asset and a deposit under
        // the wrong portfolio all end in 404.
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

        // A term deposit holds exactly one transaction — the system-managed opening Deposit — and it is
        // rewritten in place, never corrected by a second one.
        var opening = await dbContext.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken);
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

        // Re-resolved on every update, like UpdateTransaction: the stored rate always matches the
        // current start date (ADR-026).
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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, WarsawCalendar.Today(timeProvider));
    }
}
