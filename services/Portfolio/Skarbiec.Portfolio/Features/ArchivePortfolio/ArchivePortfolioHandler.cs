using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ArchivePortfolio;

public sealed class ArchivePortfolioHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<PortfolioResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(id);
        }

        portfolio.IsArchived = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return portfolio.ToResponse();
    }
}
