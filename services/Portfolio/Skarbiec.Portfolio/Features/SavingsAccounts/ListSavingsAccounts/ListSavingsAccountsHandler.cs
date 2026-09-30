using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.ListSavingsAccounts;

public sealed class ListSavingsAccountsHandler(PortfolioDbContext dbContext)
{
    /// <summary>Every savings account of the current user across all their portfolios, archived ones included (flagged), by portfolio name, then account name.</summary>
    public async Task<IReadOnlyList<SavingsAccountResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from terms in dbContext.SavingsAccounts.AsNoTracking()
                join asset in dbContext.Assets on terms.AssetId equals asset.Id
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                orderby portfolio.Name, asset.Name
                select new { Terms = terms, Asset = asset, PortfolioName = portfolio.Name, PortfolioIsArchived = portfolio.IsArchived })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => r.Terms.ToResponse(r.Asset, r.PortfolioName, r.PortfolioIsArchived))
            .ToList();
    }
}
