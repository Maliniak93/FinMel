using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.SetContributionLimit;

public sealed class SetContributionLimitHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, int year, SetContributionLimitRequest request, CancellationToken cancellationToken)
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

        if (account.Wrapper == TaxWrapper.Regular)
        {
            return AccountErrors.ContributionLimitNotAllowed;
        }

        if (request.Amount <= 0m || year is < ContributionPeriod.MinYear or > ContributionPeriod.MaxYear)
        {
            return AccountErrors.InvalidContributionLimit;
        }

        var limit = await dbContext.AccountContributionLimits
            .FirstOrDefaultAsync(l => l.AccountId == id && l.Year == year, cancellationToken);
        if (limit is null)
        {
            dbContext.AccountContributionLimits.Add(new AccountContributionLimit
            {
                Id = Guid.NewGuid(),
                AccountId = id,
                Year = year,
                Amount = request.Amount
            });
        }
        else
        {
            limit.Amount = request.Amount;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
