using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.UndoOneOffExpenseSettlement;

public static class UndoOneOffExpenseSettlementEndpoint
{
    public static IEndpointRouteBuilder MapUndoOneOffExpenseSettlementEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/one-off-expenses");

        group.MapDelete("{id:guid}/settlement", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id, UndoOneOffExpenseSettlementHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
