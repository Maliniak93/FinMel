using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.ListAccounts;

public sealed class ListAccountsHandler(PortfolioDbContext dbContext)
{
    public async Task<IReadOnlyList<AccountResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        var cashByAccount = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.AccountId != null && a.AssetClass == AssetClass.Cash)
            .ToDictionaryAsync(a => a.AccountId!.Value, cancellationToken);

        return accounts.Select(a => a.ToResponse(cashByAccount[a.Id])).ToList();
    }
}
