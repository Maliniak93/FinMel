using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsAccount;

public sealed class GetSavingsAccountHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<SavingsAccountResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // A SavingsAccount row exists only for a Savings-class asset, so any other asset id misses here.
        var row = await (
                from terms in dbContext.SavingsAccounts.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.Id == assetId && asset.PortfolioId == portfolioId
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return SavingsAccountErrors.NotFound(assetId);
        }

        return row.Terms.ToResponse(row.Asset, row.PortfolioName, row.PortfolioIsArchived);
    }
}
