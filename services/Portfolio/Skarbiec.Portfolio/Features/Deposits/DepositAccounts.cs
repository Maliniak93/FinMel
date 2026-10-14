using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Deposits;

internal static class DepositAccounts
{
    // The account a deposit is opened from: an unknown or foreign id is 404, a non-Bank one 400, an archived one 409.
    public static async Task<Result<Account>> ResolveSourceAsync(
        this PortfolioDbContext dbContext, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            return AccountErrors.NotFound(accountId);
        }

        if (account.Type != AccountType.Bank)
        {
            return AccountErrors.BankAccountRequired(accountId);
        }

        if (account.IsArchived)
        {
            return AccountErrors.Archived(accountId);
        }

        return account;
    }

    public static async Task<Result<Account>> ResolveReturnAsync(
        this PortfolioDbContext dbContext, Guid accountId, string currency, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            return AccountErrors.NotFound(accountId);
        }

        if (account.Type != AccountType.Bank || account.IsArchived)
        {
            return AccountErrors.BankAccountRequired(accountId);
        }

        if (account.Currency != currency)
        {
            return TransferErrors.CurrencyMismatch;
        }

        return account;
    }

    public static async Task<IReadOnlyDictionary<Guid, string>> LoadAccountNamesAsync(
        this PortfolioDbContext dbContext, IEnumerable<TermDeposit> deposits, CancellationToken cancellationToken)
    {
        var ids = deposits.SelectMany(d => new[] { d.SourceAccountId, d.ReturnAccountId }).Distinct().ToList();

        return await dbContext.Accounts
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
    }

    // True when the account is the return of a deposit that has not been paid out yet; only a payout writes a Withdraw on a deposit.
    public static Task<bool> IsReturnOfOpenDepositAsync(
        this PortfolioDbContext dbContext, Guid accountId, CancellationToken cancellationToken)
        => dbContext.TermDeposits.AnyAsync(
            t => t.ReturnAccountId == accountId
                && !dbContext.Transactions.Any(x => x.AssetId == t.AssetId && x.Type == TransactionType.Withdraw),
            cancellationToken);
}
