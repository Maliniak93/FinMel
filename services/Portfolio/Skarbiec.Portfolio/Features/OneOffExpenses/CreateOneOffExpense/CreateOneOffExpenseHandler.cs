using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.CreateOneOffExpense;

public sealed class CreateOneOffExpenseHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<CreateOneOffExpenseResponse>> HandleAsync(
        CreateOneOffExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = new OneOffExpense
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Amount = request.Amount,
            Date = request.Date
        };

        dbContext.OneOffExpenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateOneOffExpenseResponse { Id = expense.Id };
    }
}
