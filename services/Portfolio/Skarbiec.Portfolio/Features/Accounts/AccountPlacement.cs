using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts;

internal static class AccountPlacement
{
    // Stock and Etf sit in a Brokerage account, Bond in a Bonds account, every other class in none.
    public static AccountType? RequiredType(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Stock or AssetClass.Etf => AccountType.Brokerage,
        AssetClass.Bond => AccountType.Bonds,
        _ => null
    };

    // UpdateAsset never moves an asset, so a class change must still agree with the account it already sits in.
    public static async Task<Error?> CheckClassChangeAsync(
        this PortfolioDbContext dbContext, Asset asset, AssetClass newClass, CancellationToken cancellationToken)
    {
        if (RequiredType(newClass) is not { } requiredType)
        {
            return asset.AccountId is null ? null : AccountErrors.NotAllowed(newClass);
        }

        if (asset.AccountId is not { } id)
        {
            return AccountErrors.Required(newClass, requiredType);
        }

        var actual = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.Type)
            .FirstAsync(cancellationToken);

        return actual == requiredType ? null : AccountErrors.TypeMismatch(newClass, requiredType, actual);
    }

    public static async Task<Result<Account?>> ResolveAsync(
        this PortfolioDbContext dbContext, AssetClass assetClass, Guid? accountId, CancellationToken cancellationToken)
    {
        if (RequiredType(assetClass) is not { } requiredType)
        {
            return accountId is null
                ? Result<Account?>.Success(null)
                : AccountErrors.NotAllowed(assetClass);
        }

        if (accountId is not { } id)
        {
            return AccountErrors.Required(assetClass, requiredType);
        }

        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        if (account.Type != requiredType)
        {
            return AccountErrors.TypeMismatch(assetClass, requiredType, account.Type);
        }

        return account.IsArchived
            ? AccountErrors.Archived(id)
            : Result<Account?>.Success(account);
    }
}
