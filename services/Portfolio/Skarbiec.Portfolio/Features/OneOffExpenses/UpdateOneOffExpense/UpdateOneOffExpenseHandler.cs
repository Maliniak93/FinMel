using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.UpdateOneOffExpense;

public sealed class UpdateOneOffExpenseHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, UpdateOneOffExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await dbContext.OneOffExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return OneOffExpenseErrors.NotFound(id);
        }

        if (expense.IsSettled)
        {
            return OneOffExpenseErrors.Settled(id);
        }

        if (request.AccountId is { } accountId)
        {
            var account = await dbContext.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
            if (account is null)
            {
                return AccountErrors.NotFound(accountId);
            }

            if (account.IsArchived)
            {
                return AccountErrors.Archived(account.Id);
            }
        }

        expense.Name = request.Name;
        expense.Amount = request.Amount;
        expense.Date = request.Date;
        expense.AccountId = request.AccountId;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
