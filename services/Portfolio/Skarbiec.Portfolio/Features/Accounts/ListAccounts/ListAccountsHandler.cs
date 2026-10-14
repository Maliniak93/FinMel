using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.BankInterest;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Accounts.ListAccounts;

public sealed class ListAccountsHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AccountResponse>> HandleAsync(int? year, AccountType? type, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => type == null || a.Type == type)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        var cashByAccount = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.AccountId != null && (a.AssetClass == AssetClass.Cash || a.AssetClass == AssetClass.BankAccount))
            .ToDictionaryAsync(a => a.AccountId!.Value, cancellationToken);

        var contributions = await dbContext.LoadContributionsAsync(
            accounts, cashByAccount, ContributionPeriod.Resolve(year, timeProvider), cancellationToken);
        var bank = await dbContext.LoadBankInterestAsync(
            accounts, cashByAccount, WarsawCalendar.Today(timeProvider), cancellationToken);

        return accounts
            .Select(a => a.ToResponse(cashByAccount[a.Id]) with
            {
                Contribution = contributions.GetValueOrDefault(a.Id),
                Bank = bank.GetValueOrDefault(a.Id)?.ToResponse()
            })
            .ToList();
    }
}
