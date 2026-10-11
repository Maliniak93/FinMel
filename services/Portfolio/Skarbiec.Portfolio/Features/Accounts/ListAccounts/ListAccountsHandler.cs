using Microsoft.EntityFrameworkCore;
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

        return accounts.Select(a => a.ToResponse()).ToList();
    }
}
