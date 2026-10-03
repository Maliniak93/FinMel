using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.ListPortfolios;

public static class ListPortfoliosEndpoint
{
    public static IEndpointRouteBuilder MapListPortfoliosEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    // The default value is what makes Minimal API binding treat this as optional instead of a 400.
    private static async Task<Ok<IReadOnlyList<PortfolioResponse>>> HandleAsync(
        ListPortfoliosHandler handler, CancellationToken cancellationToken, bool includeArchived = false)
        => TypedResults.Ok(await handler.HandleAsync(includeArchived, cancellationToken));
}
