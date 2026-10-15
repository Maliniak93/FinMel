using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.DeleteRecurringIncomeChange;

public static class DeleteRecurringIncomeChangeEndpoint
{
    public static IEndpointRouteBuilder MapDeleteRecurringIncomeChangeEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-incomes");

        group.MapDelete("{id:guid}/changes/{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, string month, DeleteRecurringIncomeChangeHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, month, cancellationToken)).ToHttpResult();
}
