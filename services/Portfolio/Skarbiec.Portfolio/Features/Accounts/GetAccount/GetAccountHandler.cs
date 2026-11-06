using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.BankInterest;
using Skarbiec.Portfolio.Features.Deposits;

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
        var money = new Dictionary<Guid, Asset> { [id] = cash };
        var contributions = await dbContext.LoadContributionsAsync(
            [account], money, ContributionPeriod.Resolve(year, timeProvider), cancellationToken);
        var bank = await dbContext.LoadBankInterestAsync(
            [account], money, WarsawCalendar.Today(timeProvider), cancellationToken);

        var performance = await dbContext.LoadPerformanceAsync(
            [account], money, WarsawCalendar.Today(timeProvider), cancellationToken);

        return account.ToResponse(cash) with
        {
            Contribution = contributions.GetValueOrDefault(id),
            NetContributionsPln = performance.GetValueOrDefault(id)?.NetContributionsPln,
            PositionsCostPln = performance.GetValueOrDefault(id)?.PositionsCostPln,
            PaidInterestPln = performance.GetValueOrDefault(id)?.PaidInterestPln,
            Bank = bank.GetValueOrDefault(id)?.ToResponse()
        };
    }
}
