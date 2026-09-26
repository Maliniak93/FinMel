using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.GetDeposit;

public sealed class GetDepositHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // A TermDeposit row exists only for a Deposit-class asset, so any other asset id misses here.
        var row = await (
                from terms in dbContext.TermDeposits.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.Id == assetId && asset.PortfolioId == portfolioId
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? DepositErrors.NotFound(assetId)
            : row.Terms.ToResponse(row.Asset, row.PortfolioName, row.PortfolioIsArchived, WarsawCalendar.Today(timeProvider));
    }
}
