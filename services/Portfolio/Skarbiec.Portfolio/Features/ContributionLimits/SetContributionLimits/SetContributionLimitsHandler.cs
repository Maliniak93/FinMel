using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.ContributionLimits.SetContributionLimits;

public sealed class SetContributionLimitsHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(int year, SetContributionLimitsRequest request, CancellationToken cancellationToken)
    {
        if (year is < ContributionPeriod.MinYear or > ContributionPeriod.MaxYear
            || request.IkeAmount <= 0m || request.IkzeAmount <= 0m)
        {
            return ContributionLimitErrors.Invalid;
        }

        var existing = await dbContext.ContributionLimits.Where(l => l.Year == year).ToListAsync(cancellationToken);
        Apply(existing, year, TaxWrapper.Ike, request.IkeAmount);
        Apply(existing, year, TaxWrapper.Ikze, request.IkzeAmount);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private void Apply(List<ContributionLimit> existing, int year, TaxWrapper wrapper, decimal? amount)
    {
        var limit = existing.FirstOrDefault(l => l.Wrapper == wrapper);
        if (amount is null)
        {
            if (limit is not null)
            {
                dbContext.ContributionLimits.Remove(limit);
            }

            return;
        }

        if (limit is null)
        {
            dbContext.ContributionLimits.Add(new ContributionLimit
            {
                Id = Guid.NewGuid(),
                Wrapper = wrapper,
                Year = year,
                Amount = amount.Value
            });
        }
        else
        {
            limit.Amount = amount.Value;
        }
    }
}
