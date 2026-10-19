using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.AssetGroups.ListAssignableAssets;

public static class ListAssignableAssetsEndpoint
{
    public static IEndpointRouteBuilder MapListAssignableAssetsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapGet("assets", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<List<AssignableAssetResponse>>> HandleAsync(
        ListAssignableAssetsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
