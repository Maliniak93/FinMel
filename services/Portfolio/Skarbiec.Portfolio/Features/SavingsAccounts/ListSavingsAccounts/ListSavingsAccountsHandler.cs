using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.ListSavingsAccounts;

public sealed class ListSavingsAccountsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<SavingsAccountResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from terms in dbContext.SavingsAccounts.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                orderby portfolio.Name, asset.Name
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .ToListAsync(cancellationToken);

        var interest = await dbContext.LoadSavingsInterestStatusAsync(
            [.. rows.Select(r => r.Asset.Id)], WarsawCalendar.Today(timeProvider), cancellationToken);

        return rows
            .Select(r => r.Terms.ToResponse(
                r.Asset, r.PortfolioName, r.PortfolioIsArchived, interest.GetValueOrDefault(r.Asset.Id, SavingsInterestStatus.None)))
            .ToList();
    }
}
