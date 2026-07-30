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

    // A default value (not just a non-nullable bool) is what makes Minimal API query binding treat
    // this as optional — without one, an omitted "?includeArchived=" 400s instead of defaulting.
    private static async Task<Ok<IReadOnlyList<PortfolioResponse>>> HandleAsync(
        ListPortfoliosHandler handler, CancellationToken cancellationToken, bool includeArchived = false)
        => TypedResults.Ok(await handler.HandleAsync(includeArchived, cancellationToken));
}
