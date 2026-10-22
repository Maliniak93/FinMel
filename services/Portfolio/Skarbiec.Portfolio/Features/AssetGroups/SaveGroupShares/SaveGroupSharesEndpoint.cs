using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AssetGroups.SaveGroupShares;

public static class SaveGroupSharesEndpoint
{
    public static IEndpointRouteBuilder MapSaveGroupSharesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/asset-groups");

        group.MapPut("shares", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        SaveGroupSharesRequest request, SaveGroupSharesHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(request, cancellationToken)).ToHttpResult();
}
