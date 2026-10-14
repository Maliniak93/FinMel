using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Accounts.ArchiveAccount;

public sealed class ArchiveAccountHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<AccountResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        // Already archived: 200 and nothing to change.
        if (account.IsArchived)
        {
            return await dbContext.ToResponseAsync(account, timeProvider, cancellationToken);
        }

        var holdsQuantity = await dbContext.Assets
            .AnyAsync(a => a.AccountId == id && a.Quantity != 0m, cancellationToken);
        if (holdsQuantity)
        {
            return AccountErrors.NotEmpty(id);
        }

        if (await dbContext.IsReturnOfOpenDepositAsync(id, cancellationToken))
        {
            return AccountErrors.HasOpenDeposit(id);
        }

        account.IsArchived = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await dbContext.ToResponseAsync(account, timeProvider, cancellationToken);
    }
}
