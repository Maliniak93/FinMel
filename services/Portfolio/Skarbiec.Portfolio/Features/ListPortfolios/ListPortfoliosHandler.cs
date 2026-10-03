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

        // assetCount as a correlated subquery: no N+1 and no counter column to drift.
        var rows = await query
            .OrderBy(p => p.Name)
            .Select(p => new { Portfolio = p, AssetCount = dbContext.Assets.Count(a => a.PortfolioId == p.Id) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => r.Portfolio.ToResponse(r.AssetCount)).ToList();
    }
}
