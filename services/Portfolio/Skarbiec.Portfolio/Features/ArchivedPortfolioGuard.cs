using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

/// <summary>
/// The read-only rule for archived portfolios (archived-portfolio-out-of-net-worth): the five asset and
/// transaction writes that load an asset rather than its portfolio ask this before they write or
/// publish anything, and fail with <see cref="PortfolioErrors.Archived"/>. AddAsset already holds the
/// portfolio and checks <see cref="PortfolioEntity.IsArchived"/> directly.
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
}
