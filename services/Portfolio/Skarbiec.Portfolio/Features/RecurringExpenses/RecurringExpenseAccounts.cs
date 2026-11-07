using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.RecurringExpenses;

internal static class RecurringExpenseAccounts
{
    // The paying account of a version must be the user's own and not archived.
    public static async Task<Result> CheckPayingAccountAsync(
        this PortfolioDbContext dbContext, Guid? accountId, CancellationToken cancellationToken)
    {
        if (accountId is not { } id)
        {
            return Result.Success();
        }

        var account = await dbContext.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(id);
        }

        return account.IsArchived ? AccountErrors.Archived(id) : Result.Success();
    }
}
