using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ListAssets;

public static class ListAssetsEndpoint
{
    public static IEndpointRouteBuilder MapListAssetsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/assets");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<AssetResponse>>, ProblemHttpResult>> HandleAsync(
        ListAssetsHandler handler, CancellationToken cancellationToken, Guid? accountId = null)
        => (await handler.HandleAsync(accountId, cancellationToken)).ToHttpResult();
}
