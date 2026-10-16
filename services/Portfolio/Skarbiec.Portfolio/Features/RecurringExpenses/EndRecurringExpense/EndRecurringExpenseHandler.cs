using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.EndRecurringExpense;

public sealed class EndRecurringExpenseHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, EndRecurringExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await dbContext.RecurringExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return RecurringExpenseErrors.NotFound(id);
        }

        expense.EndDate = request.EndDate;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
