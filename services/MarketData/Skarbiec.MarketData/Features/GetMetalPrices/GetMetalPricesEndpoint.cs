using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.MarketData.Features.GetMetalPrices;

public static class GetMetalPricesEndpoint
{
    public static IEndpointRouteBuilder MapGetMetalPricesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/metal-prices");

        group.MapGet("/", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<List<MetalPriceResponse>>, ProblemHttpResult>> HandleAsync(
        GetMetalPricesHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(cancellationToken)).ToHttpResult();
}
