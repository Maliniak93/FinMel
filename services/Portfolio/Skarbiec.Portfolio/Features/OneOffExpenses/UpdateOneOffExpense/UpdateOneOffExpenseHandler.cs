using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

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

        expense.Name = request.Name;
        expense.Amount = request.Amount;
        expense.Date = request.Date;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
