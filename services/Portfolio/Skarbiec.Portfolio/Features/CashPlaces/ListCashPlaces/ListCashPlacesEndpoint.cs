using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.Portfolio.Features.CashPlaces.ListCashPlaces;

public static class ListCashPlacesEndpoint
{
    public static IEndpointRouteBuilder MapListCashPlacesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/cash-places");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Ok<CashPlacesResponse>> HandleAsync(
        ListCashPlacesHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(cancellationToken));
}
