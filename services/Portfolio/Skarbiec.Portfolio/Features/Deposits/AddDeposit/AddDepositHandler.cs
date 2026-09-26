using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
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

        // The system-managed opening transaction: the only way the principal reaches Asset.Quantity
        // (ADR-009), through the same calculator every transaction write uses.
        var opening = new Transaction
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            Type = TransactionType.Deposit,
            Quantity = request.Principal,
            UnitPriceAmount = 1m,
            Date = request.StartDate
        };

        var recomputed = TransactionQuantityCalculator.Recompute([opening]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // Last check before anything is staged, as in AddAsset: the start-date PLN rate is frozen on
        // the opening transaction (ADR-026), and MarketData being down leaves nothing half-created.
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

        // Published before SaveChangesAsync so the outbox row commits with the three rows above
        // (ADR-012); the principal is already folded into asset.Quantity.
        await positionEventPublisher.PublishCreatedAsync(asset, portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return terms.ToResponse(asset, portfolio.Name, portfolio.IsArchived, WarsawCalendar.Today(timeProvider));
    }
}
