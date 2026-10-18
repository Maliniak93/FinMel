using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.TransferPlan.AcceptTransferPlanTopUp;

public static class AcceptTransferPlanTopUpEndpoint
{
    public static IEndpointRouteBuilder MapAcceptTransferPlanTopUpEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfer-plans");

        group.MapPost("{month}/top-up/accept", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        string month, AcceptTransferPlanTopUpHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(month, cancellationToken)).ToHttpResult();
}
