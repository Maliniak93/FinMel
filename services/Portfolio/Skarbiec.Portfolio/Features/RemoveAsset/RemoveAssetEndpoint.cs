using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.RemoveAsset;

public static class RemoveAssetEndpoint
{
    public static IEndpointRouteBuilder MapRemoveAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapDelete("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid id, RemoveAssetHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, id, cancellationToken)).ToHttpResult();
}
