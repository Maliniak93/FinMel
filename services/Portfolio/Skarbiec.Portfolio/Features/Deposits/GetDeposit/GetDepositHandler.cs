using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.GetDeposit;

public sealed class GetDepositHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<DepositResponse>> HandleAsync(Guid assetId, CancellationToken cancellationToken)
    {
        // A TermDeposit row exists only for a Deposit-class asset, so any other asset id misses here.
        var row = await (
                from terms in dbContext.TermDeposits.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                where asset.Id == assetId
                select new { Terms = terms, Asset = asset })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return DepositErrors.NotFound(assetId);
        }

        var accountNames = await dbContext.LoadAccountNamesAsync([row.Terms], cancellationToken);
        var paidOutOn = await dbContext.LoadPayoutDateAsync(assetId, cancellationToken);

        return row.Terms.ToResponse(
            row.Asset, WarsawCalendar.Today(timeProvider), accountNames, paidOutOn);
    }
}
