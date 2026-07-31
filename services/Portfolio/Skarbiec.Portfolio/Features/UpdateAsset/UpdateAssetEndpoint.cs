using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public static class UpdateAssetEndpoint
{
    public static IEndpointRouteBuilder MapUpdateAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid id, UpdateAssetRequest request, UpdateAssetHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, id, request, cancellationToken)).ToHttpResult();
}
