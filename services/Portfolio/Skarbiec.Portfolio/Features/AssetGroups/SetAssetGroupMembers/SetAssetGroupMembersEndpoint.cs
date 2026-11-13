using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AssetGroups.SetAssetGroupMembers;

public static class SetAssetGroupMembersEndpoint
{
    public static IEndpointRouteBuilder MapSetAssetGroupMembersEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapPut("{groupId:guid}/members", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid groupId, SetAssetGroupMembersRequest request, SetAssetGroupMembersHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(groupId, request, cancellationToken)).ToHttpResult();
}
