using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.DeleteContributionLimit;

public sealed class DeleteContributionLimitHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, int year, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        if (account.IsArchived)
        {
            return AccountErrors.Archived(id);
        }

        var limit = await dbContext.AccountContributionLimits
            .FirstOrDefaultAsync(l => l.AccountId == id && l.Year == year, cancellationToken);
        if (limit is null)
        {
            return AccountErrors.ContributionLimitNotFound(id, year);
        }

        dbContext.AccountContributionLimits.Remove(limit);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
