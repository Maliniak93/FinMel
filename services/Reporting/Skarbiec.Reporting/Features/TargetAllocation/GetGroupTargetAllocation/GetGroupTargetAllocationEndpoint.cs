using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Reporting.Features.TargetAllocation.GetTargetAllocation;

namespace Skarbiec.Reporting.Features.TargetAllocation.GetGroupTargetAllocation;

public static class GetGroupTargetAllocationEndpoint
{
    public static IEndpointRouteBuilder MapGetGroupTargetAllocationEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/target-allocation/groups");

        group.MapGet("{groupId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<TargetAllocationResponse>> HandleAsync(
        Guid groupId, GetGroupTargetAllocationHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(groupId, cancellationToken));
}
