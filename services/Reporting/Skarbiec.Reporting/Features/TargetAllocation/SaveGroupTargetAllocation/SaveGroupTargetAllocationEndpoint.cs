using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Reporting.Features.TargetAllocation.SaveGroupTargetAllocation;

public static class SaveGroupTargetAllocationEndpoint
{
    public static IEndpointRouteBuilder MapSaveGroupTargetAllocationEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/target-allocation/groups");

        group.MapPut("{groupId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid groupId, SaveTargetAllocationRequest request, SaveGroupTargetAllocationHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(groupId, request, cancellationToken)).ToHttpResult();
}
