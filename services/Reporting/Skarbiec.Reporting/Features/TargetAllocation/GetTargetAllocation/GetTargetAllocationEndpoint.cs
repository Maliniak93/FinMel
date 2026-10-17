using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Reporting.Features.TargetAllocation.GetTargetAllocation;

public static class GetTargetAllocationEndpoint
{
    public static IEndpointRouteBuilder MapGetTargetAllocationEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/target-allocation");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<TargetAllocationResponse>> HandleAsync(
        GetTargetAllocationHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
