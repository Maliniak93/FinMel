using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.GetOverdueOccurrences;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Features.RecurringExpenses;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningTodo;

public sealed class GetPlanningTodoHandler(
    GetOverdueOccurrencesHandler overdue, PlanningMonthLoader loader, TimeProvider timeProvider)
{
    public async Task<Result<PlanningTodoResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var overdueItems = (await overdue.HandleAsync(cancellationToken)).Value.Items;

        var today = WarsawCalendar.Today(timeProvider);
        var current = YearMonth.From(today);
        var data = await loader.LoadAsync(current, current, cancellationToken);

        var items = new List<TodoItemResponse>();

        items.AddRange(overdueItems.Select(o => new TodoItemResponse
        {
            Kind = TodoItemKind.OverdueIncome,
            Name = o.Name,
            Date = o.Date,
            Amount = o.Amount,
            Currency = o.Currency,
            DaysOverdue = today.DayNumber - o.Date.DayNumber,
            DaysUntil = null
        }));

        items.AddRange(UpcomingPayments(data, today));

        return new PlanningTodoResponse { Items = items, PaymentLeadDays = data.PaymentLeadDays };
    }

    private static IEnumerable<TodoItemResponse> UpcomingPayments(PlanningMonthData data, DateOnly today)
    {
        if (data.PaymentLeadDays <= 0)
        {
            return [];
        }

        var until = today.AddDays(data.PaymentLeadDays);
        var months = new List<YearMonth>();
        for (var month = YearMonth.From(today); month <= YearMonth.From(until); month = month.AddMonths(1))
        {
            months.Add(month);
        }

        return data.Forecast.Plans
            .SelectMany(p => months
                .SelectMany(month => ReserveSchedule.PaymentsIn(month, p.Plan))
                .Where(payment => payment.Date >= today && payment.Date <= until
                    && p.Expense.InForce(payment.NominalMonth).IntervalMonths > 1)
                .Select(payment => new TodoItemResponse
                {
                    Kind = TodoItemKind.UpcomingPayment,
                    Name = p.Expense.Name,
                    Date = payment.Date,
                    Amount = payment.Amount,
                    Currency = "PLN",
                    DaysOverdue = null,
                    DaysUntil = payment.Date.DayNumber - today.DayNumber
                }))
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
