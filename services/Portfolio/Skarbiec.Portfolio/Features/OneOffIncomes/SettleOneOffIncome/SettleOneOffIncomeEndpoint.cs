using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.SettleOneOffIncome;

public static class SettleOneOffIncomeEndpoint
{
    public static IEndpointRouteBuilder MapSettleOneOffIncomeEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/one-off-incomes");

        group.MapPut("{id:guid}/settlement", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, SettleOneOffIncomeRequest request, SettleOneOffIncomeHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
