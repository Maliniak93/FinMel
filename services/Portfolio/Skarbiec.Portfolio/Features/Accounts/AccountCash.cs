using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.BankInterest;
using Skarbiec.Portfolio.Features.CashPlaces;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Accounts;

// An account's money asset: Cash for Brokerage and Bonds, BankAccount for Bank.
internal static class AccountCash
{
    public const string Currency = "PLN";

    public static readonly AssetClass[] MoneyClasses = [AssetClass.Cash, AssetClass.BankAccount];

    public static AssetClass MoneyClassOf(AccountType type) => type == AccountType.Bank ? AssetClass.BankAccount : AssetClass.Cash;

    public static bool IsAccountCash(this Asset asset) => asset.AccountId is not null && asset.AssetClass is AssetClass.Cash or AssetClass.BankAccount;

    public static Error? ManagedError(this Asset asset)
        => asset.IsAccountCash() ? AccountErrors.CashManaged(asset.Id)
            : asset.IsCashPlaceBalance() ? CashPlaceErrors.Managed(asset.Id)
            : null;

    // One error for every negative money replay, an account's cash or a cash place balance, whichever write caused it.
    public static Func<TransactionType, Error> OversellError(this Asset asset, Func<TransactionType, Error> ordinary)
        => asset.IsAccountCash() || asset.IsCashPlaceBalance() ? _ => TransferErrors.InsufficientFunds : ordinary;

    public static Task<Asset> LoadAccountCashAsync(
        this PortfolioDbContext dbContext, Guid accountId, CancellationToken cancellationToken)
        => dbContext.Assets.FirstAsync(
            a => a.AccountId == accountId && (a.AssetClass == AssetClass.Cash || a.AssetClass == AssetClass.BankAccount), cancellationToken);

    public static async Task<AccountResponse> ToResponseAsync(
        this PortfolioDbContext dbContext, Account account, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var money = await dbContext.LoadAccountCashAsync(account.Id, cancellationToken);
        var response = account.ToResponse(money);
        if (account.Type != AccountType.Bank)
        {
            return response;
        }

        var state = await dbContext.LoadBankInterestAsync(account, money, WarsawCalendar.Today(timeProvider), cancellationToken);

        return response with { Bank = state?.ToResponse() };
    }
}
