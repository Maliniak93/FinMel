using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.DeleteRecurringIncome;

public sealed class DeleteRecurringIncomeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var income = await dbContext.RecurringIncomes.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        // The versions go with it through the database cascade.
        dbContext.RecurringIncomes.Remove(income);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
