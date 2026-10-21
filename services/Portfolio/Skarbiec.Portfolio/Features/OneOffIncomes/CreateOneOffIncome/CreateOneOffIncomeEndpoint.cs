using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.CreateOneOffIncome;

public static class CreateOneOffIncomeEndpoint
{
    public static IEndpointRouteBuilder MapCreateOneOffIncomeEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/one-off-incomes");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<CreateOneOffIncomeResponse>, ProblemHttpResult>> HandleAsync(
        CreateOneOffIncomeRequest request, CreateOneOffIncomeHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/one-off-incomes/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
