using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.UpdateRecurringExpense;

public sealed class UpdateRecurringExpenseHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, UpdateRecurringExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await dbContext.RecurringExpenses
            .Include(e => e.Versions)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return RecurringExpenseErrors.NotFound(id);
        }

        if (await dbContext.RecurringExpenses.AsNoTracking().AnyAsync(e => e.Id != id && e.Name == request.Name, cancellationToken))
        {
            return RecurringExpenseErrors.DuplicateName(request.Name);
        }

        var effective = request.EffectiveMonth;
        var inForce = expense.InForce(effective);

        if (request.AccountId != inForce.AccountId)
        {
            var account = await dbContext.CheckPayingAccountAsync(request.AccountId, cancellationToken);
            if (account.IsFailure)
            {
                return account.Error;
            }
        }

        expense.Name = request.Name;
        expense.StartDate = request.StartDate;
        expense.EndDate = request.EndDate;

        var dayOfMonth = request.DayKind == RecurrenceDayKind.DayOfMonth ? request.DayOfMonth : null;
        var unchanged = inForce.Amount == request.Amount
            && inForce.IntervalMonths == request.IntervalMonths
            && inForce.DayKind == request.DayKind
            && inForce.DayOfMonth == dayOfMonth
            && inForce.Shift == request.Shift
            && inForce.AccountId == request.AccountId;

        if (!unchanged)
        {
            var target = expense.EditableFrom(effective);

            if (target is null)
            {
                dbContext.RecurringExpenseVersions.Add(new RecurringExpenseVersion
                {
                    Id = Guid.NewGuid(),
                    RecurringExpenseId = expense.Id,
                    EffectiveFrom = effective.FirstDay,
                    Amount = request.Amount,
                    IntervalMonths = request.IntervalMonths,
                    DayKind = request.DayKind,
                    DayOfMonth = dayOfMonth,
                    Shift = request.Shift,
                    AccountId = request.AccountId
                });
            }
            else
            {
                target.Amount = request.Amount;
                target.IntervalMonths = request.IntervalMonths;
                target.DayKind = request.DayKind;
                target.DayOfMonth = dayOfMonth;
                target.Shift = request.Shift;
                target.AccountId = request.AccountId;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
