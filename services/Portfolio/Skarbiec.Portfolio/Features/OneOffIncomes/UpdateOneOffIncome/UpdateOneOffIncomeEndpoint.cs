using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.UpdateOneOffIncome;

public static class UpdateOneOffIncomeEndpoint
{
    public static IEndpointRouteBuilder MapUpdateOneOffIncomeEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/one-off-incomes");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, UpdateOneOffIncomeRequest request, UpdateOneOffIncomeHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
