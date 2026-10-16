using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AllocationPlan.SaveAllocationPlanVersion;

public static class SaveAllocationPlanVersionEndpoint
{
    public static IEndpointRouteBuilder MapSaveAllocationPlanVersionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/allocation-plan");

        group.MapPut("versions/{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        string month, SaveAllocationPlanVersionRequest request, SaveAllocationPlanVersionHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(month, request, cancellationToken)).ToHttpResult();
}
