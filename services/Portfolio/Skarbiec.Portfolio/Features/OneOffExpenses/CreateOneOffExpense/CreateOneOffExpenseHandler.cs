using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.CreateOneOffExpense;

public sealed class CreateOneOffExpenseHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<CreateOneOffExpenseResponse>> HandleAsync(
        CreateOneOffExpenseRequest request, CancellationToken cancellationToken)
    {
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

        var expense = new OneOffExpense
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Amount = request.Amount,
            Date = request.Date,
            AccountId = request.AccountId
        };

        dbContext.OneOffExpenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateOneOffExpenseResponse { Id = expense.Id };
    }
}
