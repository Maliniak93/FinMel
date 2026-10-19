using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AssetGroups.UpdateAssetGroup;

public static class UpdateAssetGroupEndpoint
{
    public static IEndpointRouteBuilder MapUpdateAssetGroupEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetGroupResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, UpdateAssetGroupRequest request, UpdateAssetGroupHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult();
}
