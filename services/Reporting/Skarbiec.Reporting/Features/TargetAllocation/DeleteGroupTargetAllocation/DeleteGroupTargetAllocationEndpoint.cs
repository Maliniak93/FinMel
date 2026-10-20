using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Reporting.Features.TargetAllocation.DeleteGroupTargetAllocation;

public static class DeleteGroupTargetAllocationEndpoint
{
    public static IEndpointRouteBuilder MapDeleteGroupTargetAllocationEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/target-allocation/groups");

        group.MapDelete("{groupId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid groupId, DeleteGroupTargetAllocationHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(groupId, cancellationToken)).ToHttpResult();
}
