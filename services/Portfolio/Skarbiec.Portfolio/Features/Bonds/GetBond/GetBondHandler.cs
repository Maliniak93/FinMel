using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds.GetBond;

public sealed class GetBondHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<BondResponse>> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        // A TreasuryBond row exists only for a Bond-class asset, so any other asset id misses here.
        var row = await (
                from terms in dbContext.TreasuryBonds.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.Id == assetId && asset.PortfolioId == portfolioId
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return BondErrors.NotFound(assetId);
        }

        var funding = await dbContext.LoadFundingSourceAsync(assetId, cancellationToken);
        var settlements = await dbContext.BondInterestSettlements
            .AsNoTracking()
            .Where(s => s.AssetId == assetId)
            .ToListAsync(cancellationToken);

        return row.Terms.ToResponse(
            row.Asset, row.PortfolioName, row.PortfolioIsArchived, WarsawCalendar.Today(timeProvider), funding, settlements);
    }
}
