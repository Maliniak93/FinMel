using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AssetGroups.AssignAssetsToGroup;

public static class AssignAssetsToGroupEndpoint
{
    public static IEndpointRouteBuilder MapAssignAssetsToGroupEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapPut("assignment", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        AssignAssetsToGroupRequest request, AssignAssetsToGroupHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(request, cancellationToken)).ToHttpResult();
}
