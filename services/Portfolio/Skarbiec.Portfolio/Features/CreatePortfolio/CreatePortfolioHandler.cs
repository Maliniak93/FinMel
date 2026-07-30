using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.CreatePortfolio;

public sealed class CreatePortfolioHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<PortfolioResponse>> HandleAsync(CreatePortfolioRequest request, CancellationToken cancellationToken)
    {
        var nameTaken = await dbContext.Portfolios
            .AsNoTracking()
            .AnyAsync(p => p.Name == request.Name, cancellationToken);

        if (nameTaken)
        {
            return PortfolioErrors.DuplicateName(request.Name);
        }

        var portfolio = new PortfolioEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Currency = request.Currency
        };

        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync(cancellationToken);

        return portfolio.ToResponse();
    }
}
