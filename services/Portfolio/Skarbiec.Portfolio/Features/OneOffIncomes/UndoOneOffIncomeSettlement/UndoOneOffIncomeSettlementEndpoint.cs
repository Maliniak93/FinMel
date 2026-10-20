using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.UndoOneOffIncomeSettlement;

public static class UndoOneOffIncomeSettlementEndpoint
{
    public static IEndpointRouteBuilder MapUndoOneOffIncomeSettlementEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/one-off-incomes");

        group.MapDelete("{id:guid}/settlement", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, UndoOneOffIncomeSettlementHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
