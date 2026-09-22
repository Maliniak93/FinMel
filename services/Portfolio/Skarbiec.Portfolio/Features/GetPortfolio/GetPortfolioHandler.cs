using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetPortfolio;

public sealed class GetPortfolioHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<PortfolioResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        // assetCount as a correlated subquery inside this one query (spec-02) — see ListPortfolios.
        var row = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { Portfolio = p, AssetCount = dbContext.Assets.Count(a => a.PortfolioId == p.Id) })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? PortfolioErrors.NotFound(id)
            : row.Portfolio.ToResponse(row.AssetCount);
    }
}
