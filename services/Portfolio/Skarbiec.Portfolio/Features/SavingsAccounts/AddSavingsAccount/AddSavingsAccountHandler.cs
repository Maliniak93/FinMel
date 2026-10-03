using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;

public sealed class AddSavingsAccountHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<SavingsAccountResponse>> HandleAsync(
        Guid portfolioId, AddSavingsAccountRequest request, CancellationToken cancellationToken)
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

        if (request.OpeningDeposit is { } openingDeposit && openingDeposit.Date > WarsawCalendar.Today(timeProvider))
        {
            return SavingsAccountErrors.OpeningDepositDateInFuture;
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            AssetClass = AssetClass.Savings,
            Name = request.Name,
            Currency = request.Currency,
            ValuationMode = AssetValuationMode.CurrencyValued,
        };

        // The opening deposit is an ordinary Deposit, editable and deletable later like any other.
        Transaction? opening = null;
        if (request.OpeningDeposit is { } deposit)
        {
            opening = new Transaction
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                Type = TransactionType.Deposit,
                Quantity = deposit.Amount,
                UnitPriceAmount = 1m,
                Date = deposit.Date
            };

            var recomputed = TransactionQuantityCalculator.Recompute([opening]);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            // Last check before staging: MarketData being down leaves nothing half-created.
            var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, deposit.Date, cancellationToken);
            if (fxRateToPln.IsFailure)
            {
                return fxRateToPln.Error;
            }

            opening.FxRateToPln = fxRateToPln.Value;
            asset.Quantity = recomputed.Value;
        }

        var terms = new SavingsAccount
        {
            AssetId = asset.Id,
            BankName = request.BankName,
            AnnualInterestRatePercent = request.AnnualInterestRatePercent,
            TaxExempt = request.TaxExempt
        };

        dbContext.Assets.Add(asset);
        dbContext.SavingsAccounts.Add(terms);
        if (opening is not null)
        {
            dbContext.Transactions.Add(opening);
        }

        await positionEventPublisher.PublishCreatedAsync(asset, portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        // An opening deposit dated in an earlier month makes that month Due at once.
        var interest = await dbContext.LoadSavingsInterestStatusAsync(asset.Id, WarsawCalendar.Today(timeProvider), cancellationToken);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, interest);
    }
}
