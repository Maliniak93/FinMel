using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.GetAsset;

public static class GetAssetEndpoint
{
    public static IEndpointRouteBuilder MapGetAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapGet("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid id, GetAssetHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, id, cancellationToken)).ToHttpResult();
}
