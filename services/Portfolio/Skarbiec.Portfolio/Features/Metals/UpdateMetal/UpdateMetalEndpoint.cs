using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Metals.UpdateMetal;

public static class UpdateMetalEndpoint
{
    public static IEndpointRouteBuilder MapUpdateMetalEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/metals");

        group.MapPut("{assetId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<MetalResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, UpdateMetalRequest request, UpdateMetalHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, request, cancellationToken)).ToHttpResult();
}
