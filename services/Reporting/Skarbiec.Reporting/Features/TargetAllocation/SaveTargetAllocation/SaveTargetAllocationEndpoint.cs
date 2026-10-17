using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;

public static class SaveTargetAllocationEndpoint
{
    public static IEndpointRouteBuilder MapSaveTargetAllocationEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/target-allocation");

        group.MapPut("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        SaveTargetAllocationRequest request, SaveTargetAllocationHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(request, cancellationToken)).ToHttpResult();
}
