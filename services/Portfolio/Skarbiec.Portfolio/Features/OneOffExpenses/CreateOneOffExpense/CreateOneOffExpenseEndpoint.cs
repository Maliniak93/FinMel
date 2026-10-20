using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.CreateOneOffExpense;

public static class CreateOneOffExpenseEndpoint
{
    public static IEndpointRouteBuilder MapCreateOneOffExpenseEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/one-off-expenses");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<CreateOneOffExpenseResponse>, ProblemHttpResult>> HandleAsync(
        CreateOneOffExpenseRequest request, CreateOneOffExpenseHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/one-off-expenses/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
