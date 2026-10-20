using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.DeleteOneOffIncome;

public sealed class DeleteOneOffIncomeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var income = await dbContext.OneOffIncomes.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return OneOffIncomeErrors.NotFound(id);
        }

        dbContext.OneOffIncomes.Remove(income);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
