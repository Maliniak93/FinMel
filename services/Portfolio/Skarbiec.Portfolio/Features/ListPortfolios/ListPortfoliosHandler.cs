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

        var portfolios = await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);

        return portfolios.Select(p => p.ToResponse()).ToList();
    }
}
