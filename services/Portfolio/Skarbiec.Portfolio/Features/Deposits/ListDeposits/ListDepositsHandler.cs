using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.ListDeposits;

public sealed class ListDepositsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<DepositResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from terms in dbContext.TermDeposits.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                orderby terms.MaturityDate, asset.Name
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .ToListAsync(cancellationToken);

        var today = WarsawCalendar.Today(timeProvider);
        var depositIds = rows.Select(r => r.Asset.Id).ToList();
        var funding = await dbContext.LoadFundingSourcesAsync(depositIds, cancellationToken);
        var payouts = await dbContext.LoadPayoutsAsync(depositIds, cancellationToken);

        return rows
            .Select(r => r.Terms.ToResponse(
                r.Asset,
                r.PortfolioName,
                r.PortfolioIsArchived,
                today,
                funding.GetValueOrDefault(r.Asset.Id),
                payouts.GetValueOrDefault(r.Asset.Id)))
            .ToList();
    }
}
