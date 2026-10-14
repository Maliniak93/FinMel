using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.RestoreAccount;

public sealed class RestoreAccountHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<AccountResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        if (account.IsArchived)
        {
            account.IsArchived = false;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.ToResponseAsync(account, timeProvider, cancellationToken);
    }
}
