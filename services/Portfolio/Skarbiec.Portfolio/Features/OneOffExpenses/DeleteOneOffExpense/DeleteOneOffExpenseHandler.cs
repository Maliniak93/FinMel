using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.DeleteOneOffExpense;

public sealed class DeleteOneOffExpenseHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var expense = await dbContext.OneOffExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return OneOffExpenseErrors.NotFound(id);
        }

        dbContext.OneOffExpenses.Remove(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
