using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits.ListDeposits;

public sealed class ListDepositsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    /// <summary>Every deposit of the current user across all their portfolios, archived ones included (flagged), soonest maturity first.</summary>
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

        return rows
            .Select(r => r.Terms.ToResponse(r.Asset, r.PortfolioName, r.PortfolioIsArchived, today))
            .ToList();
    }
}
