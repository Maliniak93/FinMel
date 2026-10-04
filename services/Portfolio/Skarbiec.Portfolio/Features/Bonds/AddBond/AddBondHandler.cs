using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds.AddBond;

public sealed class AddBondHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    TimeProvider timeProvider)
{
    private const string Currency = "PLN";

    public async Task<Result<BondResponse>> HandleAsync(
        Guid portfolioId, AddBondRequest request, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(portfolioId);
        }

        if (portfolio.IsArchived)
        {
            return PortfolioErrors.Archived(portfolioId);
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            AssetClass = AssetClass.Bond,
            Name = request.Name,
            Currency = Currency,
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        var cost = request.BondCount * request.PurchasePricePerBond;

        // The system-managed opening transaction is the only way the purchase cost reaches Asset.Quantity.
        FundingTransfer? funding = null;
        Transaction opening;

        if (request.FundingAssetId is { } fundingAssetId)
        {
            var planned = await PlanFundingAsync(fundingAssetId, asset, cost, request.PurchaseDate, cancellationToken);
            if (planned.IsFailure)
            {
                return planned.Error;
            }

            funding = planned.Value;
            opening = funding.InLeg;
        }
        else
        {
            opening = new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                Type = TransactionType.Deposit,
                Quantity = cost,
                UnitPriceAmount = 1m,
                Date = request.PurchaseDate
            };
        }

        var recomputed = TransactionQuantityCalculator.Recompute([opening]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // A PLN asset: its frozen PLN rate is 1 by definition, so MarketData is not asked.
        opening.FxRateToPln = 1m;
        asset.Quantity = recomputed.Value;

        var terms = new TreasuryBond
        {
            AssetId = asset.Id,
            SeriesCode = request.SeriesCode,
            Type = request.Type,
            PurchaseDate = request.PurchaseDate,
            BondCount = request.BondCount,
            PurchasePricePerBond = request.PurchasePricePerBond,
            FirstPeriodRatePercent = request.FirstPeriodRatePercent,
            MarginPercent = request.MarginPercent,
            EarlyRedemptionFeePerBond = request.EarlyRedemptionFeePerBond,
            TaxExempt = request.TaxExempt,
            MaturityDate = BondSchedule.MaturityDate(request.Type, request.PurchaseDate)
        };

        dbContext.Assets.Add(asset);
        dbContext.Transactions.Add(opening);
        dbContext.TreasuryBonds.Add(terms);

        await positionEventPublisher.PublishCreatedAsync(asset, portfolio, cancellationToken);

        if (funding is not null)
        {
            funding.OutLeg.FxRateToPln = 1m;
            funding.Source.Quantity = funding.SourceQuantity;
            dbContext.Transactions.Add(funding.OutLeg);

            // The source publishes in the same save as both legs and the bond's own event.
            await positionEventPublisher.PublishChangedAsync(funding.Source, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A write racing on the funding Cash asset moved its xmin: nothing is saved, the user retries.
            return TransactionErrors.ConcurrentModification();
        }

        var fundingSource = funding is null ? null : new DepositFundingSource(funding.Source.Id, funding.Source.Name);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, WarsawCalendar.Today(timeProvider), fundingSource, []);
    }

    private async Task<Result<FundingTransfer>> PlanFundingAsync(
        Guid fundingAssetId, Asset bond, decimal cost, DateOnly purchaseDate, CancellationToken cancellationToken)
    {
        var source = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == fundingAssetId, cancellationToken);

        if (source is null
            || !TransferRoutes.IsAllowed(source.AssetClass, bond.AssetClass)
            || source.Currency != bond.Currency
            || source.IsArchived
            || await dbContext.IsPortfolioArchivedAsync(source.PortfolioId, cancellationToken))
        {
            return TransferErrors.InvalidCounterpart;
        }

        var (outLeg, inLeg) = TransferLegs.Create(source.Id, bond.Id, cost, purchaseDate);

        var sourceHistory = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == source.Id)
            .ToListAsync(cancellationToken);

        var sourceQuantity = TransactionQuantityCalculator.Recompute(
            [.. sourceHistory, outLeg], _ => TransferErrors.InsufficientFunds);
        if (sourceQuantity.IsFailure)
        {
            return sourceQuantity.Error;
        }

        return new FundingTransfer(source, outLeg, inLeg, sourceQuantity.Value);
    }

    private sealed record FundingTransfer(Asset Source, Transaction OutLeg, Transaction InLeg, decimal SourceQuantity);
}
