using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.GetAccount;

public sealed class GetAccountHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<AccountResponse>> HandleAsync(Guid id, int? year, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        var cash = await dbContext.LoadAccountCashAsync(id, cancellationToken);
        var contributions = await dbContext.LoadContributionsAsync(
            [account], new Dictionary<Guid, Asset> { [id] = cash }, ContributionPeriod.Resolve(year, timeProvider), cancellationToken);

        return account.ToResponse(cash) with { Contribution = contributions.GetValueOrDefault(id) };
    }
}
