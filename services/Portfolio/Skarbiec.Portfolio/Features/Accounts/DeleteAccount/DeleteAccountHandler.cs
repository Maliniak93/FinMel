using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.DeleteAccount;

public sealed class DeleteAccountHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        if (await dbContext.Assets.AnyAsync(a => a.AccountId == id, cancellationToken))
        {
            return AccountErrors.HasHistory(id);
        }

        dbContext.Accounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
