using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.UpdateOneOffIncome;

public sealed class UpdateOneOffIncomeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, UpdateOneOffIncomeRequest request, CancellationToken cancellationToken)
    {
        var income = await dbContext.OneOffIncomes.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return OneOffIncomeErrors.NotFound(id);
        }

        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(request.AccountId);
        }

        if (account.IsArchived && account.Id != income.AccountId)
        {
            return AccountErrors.Archived(account.Id);
        }

        income.Name = request.Name;
        income.Amount = request.Amount;
        income.AccountId = account.Id;
        income.Date = request.Date;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
