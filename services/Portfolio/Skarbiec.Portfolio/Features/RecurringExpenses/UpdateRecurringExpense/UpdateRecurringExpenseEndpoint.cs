using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.UpdateRecurringExpense;

public static class UpdateRecurringExpenseEndpoint
{
    public static IEndpointRouteBuilder MapUpdateRecurringExpenseEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-expenses");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, UpdateRecurringExpenseRequest request, UpdateRecurringExpenseHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
