using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.CreateRecurringIncome;

public static class CreateRecurringIncomeEndpoint
{
    public static IEndpointRouteBuilder MapCreateRecurringIncomeEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/recurring-incomes");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<CreateRecurringIncomeResponse>, ProblemHttpResult>> HandleAsync(
        CreateRecurringIncomeRequest request, CreateRecurringIncomeHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/recurring-incomes/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
