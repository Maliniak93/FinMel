using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.TransferPlan.SkipTransferPlan;

public static class SkipTransferPlanEndpoint
{
    public static IEndpointRouteBuilder MapSkipTransferPlanEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfer-plans");

        group.MapPost("{month}/skip", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        string month, SkipTransferPlanHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(month, cancellationToken)).ToHttpResult();
}
