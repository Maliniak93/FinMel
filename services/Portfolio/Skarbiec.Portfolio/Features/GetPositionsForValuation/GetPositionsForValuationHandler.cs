using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetPositionsForValuation;

/// <summary>
/// Backs Reporting's <c>DailyPricesSynced</c> consumer (T2.11) — every user's positions, not just
/// the caller's own, so it deliberately bypasses the tenancy query filter
/// (<see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{TEntity}(IQueryable{TEntity})"/>);
/// the endpoint's <c>SystemCaller</c> authorization policy is what keeps this from being reachable
/// by a normal user token (ADR-006 governs per-user access, this is the separate "who may see
/// everyone's data" boundary).
/// </summary>
public sealed class GetPositionsForValuationHandler(PortfolioDbContext dbContext)
{
    private const int DefaultPageSize = 500;
    private const int MaxPageSize = 1000;

    public async Task<PositionsForValuationPage> HandleAsync(int page, int? pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        var take = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        // Archived portfolios are hidden by the user, same as GetWealthSummary — a snapshot for
        // one shouldn't reappear on the dashboard.
        var archivedPortfolioIds = dbContext.Portfolios
            .IgnoreQueryFilters()
            .Where(p => p.IsArchived)
            .Select(p => p.Id);

        var assets = await dbContext.Assets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => !archivedPortfolioIds.Contains(a.PortfolioId))
            .OrderBy(a => a.Id)
            .Skip((page - 1) * take)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var hasMore = assets.Count > take;
        if (hasMore)
        {
            assets.RemoveAt(assets.Count - 1);
        }

        return new PositionsForValuationPage
        {
            Items = assets.Select(a => a.ToPositionForValuationResponse()).ToList(),
            HasMore = hasMore,
        };
    }
}
