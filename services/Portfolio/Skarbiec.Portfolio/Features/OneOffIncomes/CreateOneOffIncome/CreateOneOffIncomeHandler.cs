using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.CreateOneOffIncome;

public sealed class CreateOneOffIncomeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<CreateOneOffIncomeResponse>> HandleAsync(
        CreateOneOffIncomeRequest request, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);

        if (account is null)
        {
            return AccountErrors.NotFound(request.AccountId);
        }

        if (account.IsArchived)
        {
            return AccountErrors.Archived(account.Id);
        }

        var income = new OneOffIncome
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Amount = request.Amount,
            AccountId = account.Id,
            Date = request.Date
        };

        dbContext.OneOffIncomes.Add(income);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateOneOffIncomeResponse { Id = income.Id };
    }
}
