using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.AssetGroups.ListAssetGroups;

public static class ListAssetGroupsEndpoint
{
    public static IEndpointRouteBuilder MapListAssetGroupsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<List<AssetGroupResponse>>> HandleAsync(
        ListAssetGroupsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
