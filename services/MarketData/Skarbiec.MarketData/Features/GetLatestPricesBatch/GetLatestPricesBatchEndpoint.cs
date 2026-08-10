using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.MarketData.Features.GetLatestPricesBatch;

public static class GetLatestPricesBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetLatestPricesBatchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/prices");

        group.MapPost("/latest-batch", HandleAsync).RequireAuthorization(SystemCaller.PolicyName);

        return app;
    }

    private static async Task<Ok<LatestPricesBatchResponse>> HandleAsync(
        LatestPricesBatchRequest request, GetLatestPricesBatchHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
