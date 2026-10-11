using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AddAsset;

public static class AddAssetEndpoint
{
    public static IEndpointRouteBuilder MapAddAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/assets");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<AssetResponse>, ProblemHttpResult>> HandleAsync(
        AddAssetRequest request, AddAssetHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/assets/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
