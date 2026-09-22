using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ListPortfolios;

public sealed class ListPortfoliosHandler(PortfolioDbContext dbContext)
{
    public async Task<IReadOnlyList<PortfolioResponse>> HandleAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = dbContext.Portfolios.AsNoTracking();

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        // assetCount as a correlated subquery inside this one query (spec-02) — no N+1, no second
        // round trip, and no counter column to drift out of sync with the Assets table.
        var rows = await query
            .OrderBy(p => p.Name)
            .Select(p => new { Portfolio = p, AssetCount = dbContext.Assets.Count(a => a.PortfolioId == p.Id) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => r.Portfolio.ToResponse(r.AssetCount)).ToList();
    }
}
