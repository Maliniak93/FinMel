using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

/// <summary>
/// The read-only rule for archived portfolios (archived-portfolio-out-of-net-worth) and archived assets
/// (asset-archive): every write to an existing asset asks <see cref="ReadOnlyErrorAsync"/> (or
/// <see cref="ReadOnlyError"/> when it already holds the portfolio's flag) before it writes or publishes
/// anything. The portfolio wins: <see cref="PortfolioErrors.Archived"/> first, then
/// <see cref="PortfolioErrors.AssetArchived"/>. AddAsset and AddDeposit create a new asset, so they check
/// only <see cref="PortfolioEntity.IsArchived"/>; RemoveAsset checks only the portfolio, because removing
/// an archived asset stays allowed.
/// </summary>
internal static class ArchivedPortfolioGuard
{
    /// <summary>
    /// Call it only after the tenancy-scoped asset lookup succeeded, so a stranger's id still ends in
    /// 404 and never reveals the portfolio's archived state.
    /// </summary>
    public static Task<bool> IsPortfolioArchivedAsync(
        this PortfolioDbContext dbContext, Guid portfolioId, CancellationToken cancellationToken)
        => dbContext.Portfolios.AnyAsync(p => p.Id == portfolioId && p.IsArchived, cancellationToken);

    /// <summary>
    /// The conflict a write to <paramref name="asset"/> fails with, or <see langword="null"/> when it is
    /// writable. Same tenancy caveat as <see cref="IsPortfolioArchivedAsync"/>.
    /// </summary>
    public static async Task<Error?> ReadOnlyErrorAsync(
        this PortfolioDbContext dbContext, Asset asset, CancellationToken cancellationToken)
        => asset.ReadOnlyError(await dbContext.IsPortfolioArchivedAsync(asset.PortfolioId, cancellationToken));

    /// <summary>
    /// <see cref="ReadOnlyErrorAsync"/> for a slice that already loaded its portfolio's archived flag.
    /// </summary>
    public static Error? ReadOnlyError(this Asset asset, bool portfolioIsArchived)
        => portfolioIsArchived ? PortfolioErrors.Archived(asset.PortfolioId)
            : asset.IsArchived ? PortfolioErrors.AssetArchived(asset.Id)
            : null;
}
