using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Accounts;

internal static class AccountCash
{
    public const string Currency = "PLN";

    public static bool IsAccountCash(this Asset asset) => asset.AssetClass == AssetClass.Cash && asset.AccountId is not null;

    public static Error? ManagedError(this Asset asset)
        => asset.IsAccountCash() ? AccountErrors.CashManaged(asset.Id) : null;

    // One error for every negative account-cash replay, whichever write caused it.
    public static Func<TransactionType, Error> OversellError(this Asset asset, Func<TransactionType, Error> ordinary)
        => asset.IsAccountCash() ? _ => TransferErrors.InsufficientFunds : ordinary;

    public static Task<Asset> LoadAccountCashAsync(
        this PortfolioDbContext dbContext, Guid accountId, CancellationToken cancellationToken)
        => dbContext.Assets.FirstAsync(a => a.AccountId == accountId && a.AssetClass == AssetClass.Cash, cancellationToken);

    public static async Task<AccountResponse> ToResponseAsync(
        this PortfolioDbContext dbContext, Account account, CancellationToken cancellationToken)
        => account.ToResponse(await dbContext.LoadAccountCashAsync(account.Id, cancellationToken));
}
