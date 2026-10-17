using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.TransferPlan.UnskipTransferPlan;

public static class UnskipTransferPlanEndpoint
{
    public static IEndpointRouteBuilder MapUnskipTransferPlanEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfer-plans");

        group.MapDelete("{month}/skip", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        string month, UnskipTransferPlanHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(month, cancellationToken)).ToHttpResult();
}
