using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Http;

namespace Skarbiec.MarketData.Features.GetFxRatesBatch;

public static class GetFxRatesBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetFxRatesBatchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapInternalGroup("fx");

        group.MapPost("/latest-batch", HandleAsync);

        return app;
    }

    private static async Task<Ok<FxRatesBatchResponse>> HandleAsync(
        FxRatesBatchRequest request, GetFxRatesBatchHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
