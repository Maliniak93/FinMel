using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.MarketData.Features.ListBondSeries;

public static class ListBondSeriesEndpoint
{
    public static IEndpointRouteBuilder MapListBondSeriesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/bond-series");

        group.MapGet("/", HandleAsync).RequireAuthorization();

        return app;
    }

    // Defaulted, so an omitted ?onSaleOn= means today in Warsaw instead of failing as missing.
    private static async Task<Ok<IReadOnlyList<BondSeriesListItem>>> HandleAsync(
        ListBondSeriesHandler handler, CancellationToken cancellationToken, DateOnly? onSaleOn = null)
        => TypedResults.Ok(await handler.HandleAsync(onSaleOn, cancellationToken));
}
