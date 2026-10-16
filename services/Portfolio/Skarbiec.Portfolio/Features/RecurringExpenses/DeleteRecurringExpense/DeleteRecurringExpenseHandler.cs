using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.DeleteRecurringExpense;

public sealed class DeleteRecurringExpenseHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var expense = await dbContext.RecurringExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return RecurringExpenseErrors.NotFound(id);
        }

        // The versions go with it through the database cascade.
        dbContext.RecurringExpenses.Remove(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
