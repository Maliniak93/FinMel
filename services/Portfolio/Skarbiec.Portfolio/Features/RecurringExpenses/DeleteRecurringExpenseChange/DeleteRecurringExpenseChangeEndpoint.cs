using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.DeleteRecurringExpenseChange;

public static class DeleteRecurringExpenseChangeEndpoint
{
    public static IEndpointRouteBuilder MapDeleteRecurringExpenseChangeEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-expenses");

        group.MapDelete("{id:guid}/changes/{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, string month, DeleteRecurringExpenseChangeHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, month, cancellationToken)).ToHttpResult();
}
