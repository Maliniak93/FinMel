using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.ListAccounts;

public sealed class ListAccountsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AccountResponse>> HandleAsync(int? year, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        var cashByAccount = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.AccountId != null && a.AssetClass == AssetClass.Cash)
            .ToDictionaryAsync(a => a.AccountId!.Value, cancellationToken);

        var contributions = await dbContext.LoadContributionsAsync(
            accounts, cashByAccount, ContributionPeriod.Resolve(year, timeProvider), cancellationToken);

        return accounts
            .Select(a => a.ToResponse(cashByAccount[a.Id]) with { Contribution = contributions.GetValueOrDefault(a.Id) })
            .ToList();
    }
}
