using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CashAccounts.ListCashAccounts;

public sealed class ListCashAccountsHandler(PortfolioDbContext dbContext)
{
    private const string BaseCurrency = "PLN";

    public async Task<CashAccountsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var accounts = await (
                from asset in dbContext.Assets.AsNoTracking()
                join portfolio in dbContext.Portfolios on asset.PortfolioId equals portfolio.Id
                where asset.AssetClass == AssetClass.Cash && !asset.IsArchived && !portfolio.IsArchived
                orderby portfolio.Name, asset.Name
                select new CashAccountResponse
                {
                    AssetId = asset.Id,
                    PortfolioId = portfolio.Id,
                    PortfolioName = portfolio.Name,
                    Name = asset.Name,
                    Currency = asset.Currency,
                    Balance = asset.Quantity
                })
            .ToListAsync(cancellationToken);

        var totals = accounts
            .GroupBy(a => a.Currency)
            .Select(g => new CashTotalResponse { Currency = g.Key, Balance = g.Sum(a => a.Balance) })
            .OrderBy(t => t.Currency == BaseCurrency ? 0 : 1)
            .ThenBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();

        return new CashAccountsResponse { Accounts = accounts, Totals = totals };
    }
}
