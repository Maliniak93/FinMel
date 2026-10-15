using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.EndRecurringIncome;

public sealed class EndRecurringIncomeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, EndRecurringIncomeRequest request, CancellationToken cancellationToken)
    {
        var income = await dbContext.RecurringIncomes.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        if (request.EndDate < income.StartDate)
        {
            return RecurringIncomeErrors.EndBeforeStart(income.StartDate);
        }

        income.EndDate = request.EndDate;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
