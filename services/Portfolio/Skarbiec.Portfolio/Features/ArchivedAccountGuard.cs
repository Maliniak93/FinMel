using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.CashPlaces;

namespace Skarbiec.Portfolio.Features;

internal static class ArchivedAccountGuard
{
    // Call only after the tenancy-scoped lookup, so a foreign id still ends in 404.
    public static async Task<bool> IsAccountArchivedAsync(
        this PortfolioDbContext dbContext, Guid? accountId, CancellationToken cancellationToken)
        => accountId is { } id && await dbContext.Accounts.AnyAsync(a => a.Id == id && a.IsArchived, cancellationToken);

    public static async Task<Error?> AccountArchivedErrorAsync(
        this PortfolioDbContext dbContext, Asset asset, CancellationToken cancellationToken)
        => await dbContext.IsAccountArchivedAsync(asset.AccountId, cancellationToken) ? AccountErrors.Archived(asset.AccountId!.Value) : null;

    public static async Task<Error?> ReadOnlyErrorAsync(
        this PortfolioDbContext dbContext, Asset asset, CancellationToken cancellationToken)
    {
        if (asset.CashPlaceId is { } placeId
            && await dbContext.CashPlaces.AnyAsync(p => p.Id == placeId && p.IsArchived, cancellationToken))
        {
            return CashPlaceErrors.Archived(placeId);
        }

        return asset.ReadOnlyError(await dbContext.IsAccountArchivedAsync(asset.AccountId, cancellationToken));
    }

    public static Error? ReadOnlyError(this Asset asset, bool accountIsArchived)
        => accountIsArchived ? AccountErrors.Archived(asset.AccountId!.Value)
            : asset.IsArchived ? AssetErrors.AssetArchived(asset.Id)
            : null;
}
