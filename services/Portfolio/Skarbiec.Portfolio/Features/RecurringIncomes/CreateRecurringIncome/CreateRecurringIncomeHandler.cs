using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.CreateRecurringIncome;

public sealed class CreateRecurringIncomeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<CreateRecurringIncomeResponse>> HandleAsync(
        CreateRecurringIncomeRequest request, CancellationToken cancellationToken)
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

        if (await dbContext.RecurringIncomes.AsNoTracking().AnyAsync(i => i.Name == request.Name, cancellationToken))
        {
            return RecurringIncomeErrors.DuplicateName(request.Name);
        }

        var income = new RecurringIncome
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };
        income.Versions.Add(new RecurringIncomeVersion
        {
            Id = Guid.NewGuid(),
            RecurringIncomeId = income.Id,
            EffectiveFrom = YearMonth.From(request.StartDate).FirstDay,
            Amount = request.Amount,
            AccountId = account.Id,
            IntervalMonths = request.IntervalMonths,
            DayKind = request.DayKind,
            DayOfMonth = request.DayKind == RecurrenceDayKind.DayOfMonth ? request.DayOfMonth : null,
            Shift = request.Shift
        });

        dbContext.RecurringIncomes.Add(income);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateRecurringIncomeResponse { Id = income.Id };
    }
}
