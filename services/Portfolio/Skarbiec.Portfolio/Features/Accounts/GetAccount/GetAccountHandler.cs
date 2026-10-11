using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.GetAccount;

public sealed class GetAccountHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AccountResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return account is null
            ? AccountErrors.NotFound(id)
            : account.ToResponse();
    }
}
