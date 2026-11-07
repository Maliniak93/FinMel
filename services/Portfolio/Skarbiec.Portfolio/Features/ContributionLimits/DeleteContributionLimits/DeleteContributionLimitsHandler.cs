using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ContributionLimits.DeleteContributionLimits;

public sealed class DeleteContributionLimitsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(int year, CancellationToken cancellationToken)
    {
        var limits = await dbContext.ContributionLimits.Where(l => l.Year == year).ToListAsync(cancellationToken);
        if (limits.Count == 0)
        {
            return ContributionLimitErrors.NotFound(year);
        }

        dbContext.ContributionLimits.RemoveRange(limits);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
