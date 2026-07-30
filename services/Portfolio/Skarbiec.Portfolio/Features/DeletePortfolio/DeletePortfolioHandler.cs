using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.DeletePortfolio;

public sealed class DeletePortfolioHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(id);
        }

        if (portfolio.AssetCount > 0)
        {
            return PortfolioErrors.HasAssets(id);
        }

        dbContext.Portfolios.Remove(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
