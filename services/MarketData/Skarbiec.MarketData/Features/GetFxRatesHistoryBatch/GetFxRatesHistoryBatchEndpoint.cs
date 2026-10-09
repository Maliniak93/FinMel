using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Http;

namespace Skarbiec.MarketData.Features.GetFxRatesHistoryBatch;

public static class GetFxRatesHistoryBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetFxRatesHistoryBatchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapInternalGroup("fx");

        group.MapPost("/history-batch", HandleAsync);

        return app;
    }

    private static async Task<Ok<FxRatesHistoryBatchResponse>> HandleAsync(
        FxRatesHistoryBatchRequest request, GetFxRatesHistoryBatchHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
