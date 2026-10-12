using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.UpdateAccount;

public sealed class UpdateAccountHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<AccountResponse>> HandleAsync(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        if (!string.Equals(account.Name, request.Name, StringComparison.Ordinal))
        {
            var nameTaken = await dbContext.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id != id && a.Name == request.Name, cancellationToken);

            if (nameTaken)
            {
                return AccountErrors.DuplicateName(request.Name);
            }
        }

        var cash = await dbContext.LoadAccountCashAsync(id, cancellationToken);

        // The cash carries the account's name; no event, since AssetPositionChanged holds no name.
        account.Name = request.Name;
        cash.Name = request.Name;

        await dbContext.SaveChangesAsync(cancellationToken);

        return account.ToResponse(cash);
    }
}
