using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ArchiveAsset;

public static class ArchiveAssetEndpoint
{
    public static IEndpointRouteBuilder MapArchiveAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapPost("{id:guid}/archive", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid id, ArchiveAssetHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, id, cancellationToken)).ToHttpResult();
}
