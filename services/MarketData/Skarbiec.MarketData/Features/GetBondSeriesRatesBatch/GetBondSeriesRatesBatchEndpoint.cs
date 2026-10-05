using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Http;

namespace Skarbiec.MarketData.Features.GetBondSeriesRatesBatch;

public static class GetBondSeriesRatesBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetBondSeriesRatesBatchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapInternalGroup("bond-series");

        group.MapPost("/rates-batch", HandleAsync);

        return app;
    }

    private static async Task<Ok<BondSeriesRatesBatchResponse>> HandleAsync(
        BondSeriesRatesBatchRequest request, GetBondSeriesRatesBatchHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
