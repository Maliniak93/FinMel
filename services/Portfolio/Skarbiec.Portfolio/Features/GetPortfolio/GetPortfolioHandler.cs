using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetPortfolio;

public sealed class GetPortfolioHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<PortfolioResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return portfolio is null
            ? PortfolioErrors.NotFound(id)
            : portfolio.ToResponse();
    }
}
