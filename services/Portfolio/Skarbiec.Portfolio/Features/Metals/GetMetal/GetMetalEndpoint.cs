using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Metals.GetMetal;

public static class GetMetalEndpoint
{
    public static IEndpointRouteBuilder MapGetMetalEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/metals");

        group.MapGet("{assetId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<MetalResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, GetMetalHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, cancellationToken)).ToHttpResult();
}
