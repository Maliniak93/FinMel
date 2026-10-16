using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.CreateRecurringExpense;

public sealed class CreateRecurringExpenseHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<CreateRecurringExpenseResponse>> HandleAsync(
        CreateRecurringExpenseRequest request, CancellationToken cancellationToken)
    {
        if (await dbContext.RecurringExpenses.AsNoTracking().AnyAsync(e => e.Name == request.Name, cancellationToken))
        {
            return RecurringExpenseErrors.DuplicateName(request.Name);
        }

        var createdOn = WarsawCalendar.Today(timeProvider);
        var firstMonth = YearMonth.From(request.StartDate);
        var createdMonth = YearMonth.From(createdOn);

        var expense = new RecurringExpense
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedOn = createdOn
        };
        expense.Versions.Add(new RecurringExpenseVersion
        {
            Id = Guid.NewGuid(),
            RecurringExpenseId = expense.Id,
            EffectiveFrom = (firstMonth < createdMonth ? firstMonth : createdMonth).FirstDay,
            Amount = request.Amount,
            IntervalMonths = request.IntervalMonths,
            DayKind = request.DayKind,
            DayOfMonth = request.DayKind == RecurrenceDayKind.DayOfMonth ? request.DayOfMonth : null,
            Shift = request.Shift
        });

        dbContext.RecurringExpenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateRecurringExpenseResponse { Id = expense.Id };
    }
}
