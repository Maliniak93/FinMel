using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.GetOverdueOccurrences;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Features.RecurringExpenses;
using Skarbiec.Portfolio.Features.TransferPlan;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningTodo;

public sealed class GetPlanningTodoHandler(
    PortfolioDbContext dbContext, GetOverdueOccurrencesHandler overdue, PlanningMonthLoader loader, TimeProvider timeProvider)
{
    public async Task<Result<PlanningTodoResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var overdueItems = (await overdue.HandleAsync(cancellationToken)).Value.Items;

        var earliestIncome = await dbContext.RecurringIncomes
            .AsNoTracking()
            .MinAsync(i => (DateOnly?)i.CreatedOn, cancellationToken);

        var today = WarsawCalendar.Today(timeProvider);
        var current = YearMonth.From(today);
        var first = earliestIncome is { } created ? YearMonth.From(created) : current;
        var data = await loader.LoadAsync(first, current, cancellationToken);

        var items = new List<TodoItemResponse>();

        items.AddRange(overdueItems.Select(o => new TodoItemResponse
        {
            Kind = TodoItemKind.OverdueIncome,
            Name = o.Name,
            Date = o.Date,
            Month = null,
            Amount = o.Amount,
            Currency = o.Currency,
            DaysOverdue = today.DayNumber - o.Date.DayNumber,
            DaysUntil = null,
            IsTopUp = null,
            UncheckedRows = null,
            TotalRows = null
        }));

        if (earliestIncome is not null)
        {
            for (var month = first; month <= current; month = month.AddMonths(1))
            {
                items.AddRange(TransferPlanItems(month, PlanningMonthBuilder.Build(data, month).TransferPlan));
            }
        }

        items.AddRange(UpcomingPayments(data, today));

        return new PlanningTodoResponse { Items = items, PaymentLeadDays = data.PaymentLeadDays };
    }

    private static IEnumerable<TodoItemResponse> TransferPlanItems(YearMonth month, TransferPlanResponse plan)
    {
        if (plan.Status == TransferPlanStatus.Preview && plan.ToInvestPln > 0m)
        {
            yield return TransferPlanItem(TodoItemKind.TransferPlanToAccept, month, plan.ToInvestPln, false, null, null);
        }

        if (plan.TopUp is { } topUp && topUp.Rows.Sum(r => r.AmountPln) is var topUpPln and > 0m)
        {
            yield return TransferPlanItem(TodoItemKind.TransferPlanToAccept, month, topUpPln, true, null, null);
        }

        if (plan.Status == TransferPlanStatus.Accepted)
        {
            var open = plan.Rows.Count(r => r.Checked == false);
            if (open > 0)
            {
                yield return TransferPlanItem(
                    TodoItemKind.TransferPlanUnchecked, month, null, null, open, plan.Rows.Count);
            }
        }
    }

    private static TodoItemResponse TransferPlanItem(
        TodoItemKind kind, YearMonth month, decimal? amount, bool? isTopUp, int? uncheckedRows, int? totalRows) =>
        new()
        {
            Kind = kind,
            Name = null,
            Date = null,
            Month = month.ToString(),
            Amount = amount,
            Currency = null,
            DaysOverdue = null,
            DaysUntil = null,
            IsTopUp = isTopUp,
            UncheckedRows = uncheckedRows,
            TotalRows = totalRows
        };

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
                    Month = null,
                    Amount = payment.Amount,
                    Currency = "PLN",
                    DaysOverdue = null,
                    DaysUntil = payment.Date.DayNumber - today.DayNumber,
                    IsTopUp = null,
                    UncheckedRows = null,
                    TotalRows = null
                }))
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
