using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AllocationPlan.DeleteAllocationPlanVersion;

public static class DeleteAllocationPlanVersionEndpoint
{
    public static IEndpointRouteBuilder MapDeleteAllocationPlanVersionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/allocation-plan");

        group.MapDelete("versions/{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        string month, DeleteAllocationPlanVersionHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(month, cancellationToken)).ToHttpResult();
}
