using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ListAssets;

public sealed class ListAssetsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<IReadOnlyList<AssetResponse>>> HandleAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var portfolioExists = await dbContext.Portfolios
            .AsNoTracking()
            .AnyAsync(p => p.Id == portfolioId, cancellationToken);

        if (!portfolioExists)
        {
            return PortfolioErrors.NotFound(portfolioId);
        }

        var assets = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.PortfolioId == portfolioId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        IReadOnlyList<AssetResponse> response = assets.Select(a => a.ToResponse()).ToList();
        return Result<IReadOnlyList<AssetResponse>>.Success(response);
    }
}
