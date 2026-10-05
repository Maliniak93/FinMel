using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Bonds.ListBonds;

public sealed class ListBondsHandler(PortfolioDbContext dbContext, IBondRateLookupClient rateLookup, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<BondResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from terms in dbContext.TreasuryBonds.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                orderby terms.MaturityDate, asset.Name
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .ToListAsync(cancellationToken);

        var today = WarsawCalendar.Today(timeProvider);
        var assetIds = rows.Select(r => r.Asset.Id).ToList();
        var funding = await dbContext.LoadFundingSourcesAsync(assetIds, cancellationToken);
        var settlements = (await dbContext.BondInterestSettlements
                .AsNoTracking()
                .Where(s => assetIds.Contains(s.AssetId))
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.AssetId);
        var links = await dbContext.LoadBondLinksAsync([.. rows.Select(r => r.Terms)], cancellationToken);

        var bonds = rows
            .Select(r =>
            {
                List<BondInterestSettlement> bondSettlements = [.. settlements[r.Asset.Id]];
                var response = r.Terms.ToResponse(
                    r.Asset,
                    r.PortfolioName,
                    r.PortfolioIsArchived,
                    today,
                    funding.GetValueOrDefault(r.Asset.Id),
                    bondSettlements,
                    [.. links.Redemptions[r.Asset.Id]],
                    links.SwappedFrom.GetValueOrDefault(r.Asset.Id));

                return new BondEstimateInput(response, r.Terms, bondSettlements);
            })
            .ToList();

        return await rateLookup.WithEstimatesAsync(bonds, today, cancellationToken);
    }
}
