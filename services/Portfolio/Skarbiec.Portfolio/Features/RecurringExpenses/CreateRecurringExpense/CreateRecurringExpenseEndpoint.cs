using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.CreateRecurringExpense;

public static class CreateRecurringExpenseEndpoint
{
    public static IEndpointRouteBuilder MapCreateRecurringExpenseEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-expenses");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<CreateRecurringExpenseResponse>, ProblemHttpResult>> HandleAsync(
        CreateRecurringExpenseRequest request, CreateRecurringExpenseHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/recurring-expenses/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
