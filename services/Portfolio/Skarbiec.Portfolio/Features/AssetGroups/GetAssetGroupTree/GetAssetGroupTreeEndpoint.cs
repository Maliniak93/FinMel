using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.AssetGroups.GetAssetGroupTree;

public static class GetAssetGroupTreeEndpoint
{
    public static IEndpointRouteBuilder MapGetAssetGroupTreeEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapGet("tree", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<List<AssetGroupTreeItem>>> HandleAsync(
        GetAssetGroupTreeHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
