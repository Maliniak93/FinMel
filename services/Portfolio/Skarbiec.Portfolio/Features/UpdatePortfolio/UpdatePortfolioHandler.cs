using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.UpdatePortfolio;

public sealed class UpdatePortfolioHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<PortfolioResponse>> HandleAsync(Guid id, UpdatePortfolioRequest request, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(id);
        }

        if (!string.Equals(portfolio.Name, request.Name, StringComparison.Ordinal))
        {
            var nameTaken = await dbContext.Portfolios
                .AsNoTracking()
                .AnyAsync(p => p.Id != id && p.Name == request.Name, cancellationToken);

            if (nameTaken)
            {
                return PortfolioErrors.DuplicateName(request.Name);
            }
        }

        portfolio.Name = request.Name;
        portfolio.Description = request.Description;
        portfolio.Currency = request.Currency;

        await dbContext.SaveChangesAsync(cancellationToken);

        return portfolio.ToResponse();
    }
}
