using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Metals.AddMetal;

public static class AddMetalEndpoint
{
    public static IEndpointRouteBuilder MapAddMetalEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/metals");

        group.MapPost("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<MetalResponse>, ProblemHttpResult>> HandleAsync(
        AddMetalRequest request, AddMetalHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/portfolio/metals/{result.Value.AssetId}", result.Value)
            : result.Error.ToProblem();
    }
}
