using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AddAsset;

public static class AddAssetEndpoint
{
    public static IEndpointRouteBuilder MapAddAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, AddAssetRequest request, AddAssetHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/portfolios/{portfolioId}/assets/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
