using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.UpdateRecurringIncome;

public sealed class UpdateRecurringIncomeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, UpdateRecurringIncomeRequest request, CancellationToken cancellationToken)
    {
        var income = await dbContext.RecurringIncomes
            .Include(i => i.Versions)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(request.AccountId);
        }

        var effective = request.EffectiveMonth;
        var inForce = income.InForce(effective);

        if (account.IsArchived && account.Id != inForce.AccountId)
        {
            return AccountErrors.Archived(account.Id);
        }

        if (await dbContext.RecurringIncomes.AsNoTracking().AnyAsync(i => i.Id != id && i.Name == request.Name, cancellationToken))
        {
            return RecurringIncomeErrors.DuplicateName(request.Name);
        }

        income.Name = request.Name;
        income.StartDate = request.StartDate;
        income.EndDate = request.EndDate;

        var dayOfMonth = request.DayKind == RecurrenceDayKind.DayOfMonth ? request.DayOfMonth : null;
        var unchanged = inForce.Amount == request.Amount
            && inForce.AccountId == account.Id
            && inForce.IntervalMonths == request.IntervalMonths
            && inForce.DayKind == request.DayKind
            && inForce.DayOfMonth == dayOfMonth
            && inForce.Shift == request.Shift;

        if (!unchanged)
        {
            var first = income.Versions.OrderBy(v => v.EffectiveFrom).First();
            var target = effective <= YearMonth.From(first.EffectiveFrom) || effective < YearMonth.From(request.StartDate)
                ? first
                : income.Versions.FirstOrDefault(v => v.EffectiveFrom == effective.FirstDay);

            if (target is null)
            {
                target = new RecurringIncomeVersion
                {
                    Id = Guid.NewGuid(),
                    RecurringIncomeId = income.Id,
                    EffectiveFrom = effective.FirstDay,
                    Amount = request.Amount,
                    AccountId = account.Id,
                    IntervalMonths = request.IntervalMonths,
                    DayKind = request.DayKind,
                    DayOfMonth = dayOfMonth,
                    Shift = request.Shift
                };
                dbContext.RecurringIncomeVersions.Add(target);
            }
            else
            {
                target.Amount = request.Amount;
                target.AccountId = account.Id;
                target.IntervalMonths = request.IntervalMonths;
                target.DayKind = request.DayKind;
                target.DayOfMonth = dayOfMonth;
                target.Shift = request.Shift;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
