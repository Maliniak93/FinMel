using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

internal static class ArchivedPortfolioGuard
{
    // Call only after the tenancy-scoped lookup, so a stranger's id still ends in 404.
    public static Task<bool> IsPortfolioArchivedAsync(
        this PortfolioDbContext dbContext, Guid portfolioId, CancellationToken cancellationToken)
        => dbContext.Portfolios.AnyAsync(p => p.Id == portfolioId && p.IsArchived, cancellationToken);

    public static async Task<Error?> ReadOnlyErrorAsync(
        this PortfolioDbContext dbContext, Asset asset, CancellationToken cancellationToken)
        => asset.ReadOnlyError(await dbContext.IsPortfolioArchivedAsync(asset.PortfolioId, cancellationToken));

    public static Error? ReadOnlyError(this Asset asset, bool portfolioIsArchived)
        => portfolioIsArchived ? PortfolioErrors.Archived(asset.PortfolioId)
            : asset.IsArchived ? PortfolioErrors.AssetArchived(asset.Id)
            : null;
}
