using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AssetGroups.CreateAssetGroup;

public static class CreateAssetGroupEndpoint
{
    public static IEndpointRouteBuilder MapCreateAssetGroupEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<AssetGroupResponse>, ProblemHttpResult>> HandleAsync(
        CreateAssetGroupRequest request, CreateAssetGroupHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/asset-groups/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
