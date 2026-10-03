using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Deposits.AddDeposit;

public sealed class AddDepositHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(
        Guid portfolioId, AddDepositRequest request, CancellationToken cancellationToken)
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
            AssetClass = AssetClass.Deposit,
            Name = request.Name,
            Currency = request.Currency,
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        // The system-managed opening transaction is the only way the principal reaches Asset.Quantity.
        FundingTransfer? funding = null;
        Transaction opening;

        if (request.FundingAssetId is { } fundingAssetId)
        {
            var planned = await PlanFundingAsync(fundingAssetId, asset, request, cancellationToken);
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
                Quantity = request.Principal,
                UnitPriceAmount = 1m,
                Date = request.StartDate
            };
        }

        var recomputed = TransactionQuantityCalculator.Recompute([opening]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // Last check before staging: MarketData being down leaves nothing half-created; both legs share the one rate.
        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.StartDate, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        opening.FxRateToPln = fxRateToPln.Value;
        asset.Quantity = recomputed.Value;

        var terms = new TermDeposit
        {
            AssetId = asset.Id,
            BankName = request.BankName,
            Principal = request.Principal,
            StartDate = request.StartDate,
            TermLength = request.TermLength,
            TermUnit = request.TermUnit,
            MaturityDate = DepositInterestMath.MaturityDate(request.StartDate, request.TermLength, request.TermUnit),
            AnnualInterestRatePercent = request.AnnualInterestRatePercent,
            Capitalization = request.Capitalization,
            TaxExempt = request.TaxExempt,
            EarlyBreakInterestLossPercent = request.EarlyBreakInterestLossPercent
        };

        dbContext.Assets.Add(asset);
        dbContext.Transactions.Add(opening);
        dbContext.TermDeposits.Add(terms);

        await positionEventPublisher.PublishCreatedAsync(asset, portfolio, cancellationToken);

        if (funding is not null)
        {
            funding.OutLeg.FxRateToPln = fxRateToPln.Value;
            funding.Source.Quantity = funding.SourceQuantity;
            dbContext.Transactions.Add(funding.OutLeg);

            // The source publishes in the same save as both legs and the deposit's own event.
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

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, WarsawCalendar.Today(timeProvider), fundingSource, payout: null);
    }

    private async Task<Result<FundingTransfer>> PlanFundingAsync(
        Guid fundingAssetId, Asset deposit, AddDepositRequest request, CancellationToken cancellationToken)
    {
        var source = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == fundingAssetId, cancellationToken);

        if (source is null
            || !TransferRoutes.IsAllowed(source.AssetClass, deposit.AssetClass)
            || source.Currency != deposit.Currency
            || source.IsArchived
            || await dbContext.IsPortfolioArchivedAsync(source.PortfolioId, cancellationToken))
        {
            return TransferErrors.InvalidCounterpart;
        }

        var (outLeg, inLeg) = TransferLegs.Create(source.Id, deposit.Id, request.Principal, request.StartDate);

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
