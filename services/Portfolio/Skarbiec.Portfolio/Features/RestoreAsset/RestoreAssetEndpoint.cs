using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RestoreAsset;

public static class RestoreAssetEndpoint
{
    public static IEndpointRouteBuilder MapRestoreAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/assets");

        group.MapPost("{id:guid}/restore", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, RestoreAssetHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
