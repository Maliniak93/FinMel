using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Http;

namespace Skarbiec.MarketData.Features.GetPricesHistoryBatch;

public static class GetPricesHistoryBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetPricesHistoryBatchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapInternalGroup("prices");

        group.MapPost("/history-batch", HandleAsync);

        return app;
    }

    private static async Task<Ok<PricesHistoryBatchResponse>> HandleAsync(
        PricesHistoryBatchRequest request, GetPricesHistoryBatchHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
