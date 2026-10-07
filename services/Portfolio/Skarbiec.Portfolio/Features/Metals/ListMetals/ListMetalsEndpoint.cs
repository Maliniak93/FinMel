using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.Metals.ListMetals;

public static class ListMetalsEndpoint
{
    public static IEndpointRouteBuilder MapListMetalsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/metals");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<IReadOnlyList<MetalResponse>>> HandleAsync(
        ListMetalsHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
