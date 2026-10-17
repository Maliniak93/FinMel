using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.TransferPlan.CheckTransferPlanRow;

public static class CheckTransferPlanRowEndpoint
{
    public static IEndpointRouteBuilder MapCheckTransferPlanRowEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfer-plans");

        group.MapPut("rows/{rowId:guid}/checked", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid rowId, CheckTransferPlanRowRequest request, CheckTransferPlanRowHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(rowId, request, cancellationToken)).ToHttpResult();
}
