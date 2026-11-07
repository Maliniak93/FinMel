using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ContributionLimits.ListContributionLimits;

public sealed record ContributionLimitsResponse
{
    public required int Year { get; init; }
    public decimal? IkeAmount { get; init; }
    public decimal? IkzeAmount { get; init; }
}

public sealed class ListContributionLimitsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<List<ContributionLimitsResponse>>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.ContributionLimits.AsNoTracking().ToListAsync(cancellationToken);

        return rows
            .GroupBy(l => l.Year)
            .OrderByDescending(g => g.Key)
            .Select(g => new ContributionLimitsResponse
            {
                Year = g.Key,
                IkeAmount = g.FirstOrDefault(l => l.Wrapper == TaxWrapper.Ike)?.Amount,
                IkzeAmount = g.FirstOrDefault(l => l.Wrapper == TaxWrapper.Ikze)?.Amount
            })
            .ToList();
    }
}
