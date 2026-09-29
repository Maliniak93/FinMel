using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RestoreAsset;

public static class RestoreAssetEndpoint
{
    public static IEndpointRouteBuilder MapRestoreAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapPost("{id:guid}/restore", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid id, RestoreAssetHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, id, cancellationToken)).ToHttpResult();
}
