using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ListAssets;

public static class ListAssetsEndpoint
{
    public static IEndpointRouteBuilder MapListAssetsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<AssetResponse>>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, ListAssetsHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, cancellationToken)).ToHttpResult();
}
