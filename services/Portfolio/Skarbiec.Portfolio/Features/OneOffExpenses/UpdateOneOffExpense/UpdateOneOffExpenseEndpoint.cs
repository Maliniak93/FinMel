using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.UpdateOneOffExpense;

public static class UpdateOneOffExpenseEndpoint
{
    public static IEndpointRouteBuilder MapUpdateOneOffExpenseEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/one-off-expenses");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, UpdateOneOffExpenseRequest request, UpdateOneOffExpenseHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
