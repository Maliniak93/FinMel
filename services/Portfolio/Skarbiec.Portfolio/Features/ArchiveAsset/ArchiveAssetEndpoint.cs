using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ArchiveAsset;

public static class ArchiveAssetEndpoint
{
    public static IEndpointRouteBuilder MapArchiveAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/assets");

        group.MapPost("{id:guid}/archive", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, ArchiveAssetHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
