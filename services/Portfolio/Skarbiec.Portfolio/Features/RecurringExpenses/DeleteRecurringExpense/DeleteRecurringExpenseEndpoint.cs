using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.DeleteRecurringExpense;

public static class DeleteRecurringExpenseEndpoint
{
    public static IEndpointRouteBuilder MapDeleteRecurringExpenseEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-expenses");

        group.MapDelete("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, DeleteRecurringExpenseHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
