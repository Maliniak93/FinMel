using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest;

internal sealed record BankAccountMoney(Account Account, Asset Money);

internal static class BankInterestLookup
{
    // Tenancy-scoped lookup first, so a stranger's account is 404 whatever else is wrong with it.
    public static async Task<Result<BankAccountMoney>> FindOpenBankAccountAsync(
        this PortfolioDbContext dbContext, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(accountId);
        }

        if (account.Type != AccountType.Bank)
        {
            return BankInterestErrors.NotSupported;
        }

        if (account.IsArchived)
        {
            return AccountErrors.Archived(accountId);
        }

        return new BankAccountMoney(account, await dbContext.LoadAccountCashAsync(accountId, cancellationToken));
    }
}
