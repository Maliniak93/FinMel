using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsAccount;

public sealed class GetSavingsAccountHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<SavingsAccountResponse>> HandleAsync(Guid assetId, CancellationToken cancellationToken)
    {
        // A SavingsAccount row exists only for a Savings-class asset, so any other asset id misses here.
        var row = await (
                from terms in dbContext.SavingsAccounts.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                where asset.Id == assetId
                select new { Terms = terms, Asset = asset })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return SavingsAccountErrors.NotFound(assetId);
        }

        var interest = await dbContext.LoadSavingsInterestStatusAsync(assetId, WarsawCalendar.Today(timeProvider), cancellationToken);

        return row.Terms.ToResponse(row.Asset, interest);
    }
}
