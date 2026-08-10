using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.MarketData.Features.GetFxRatesBatch;

public static class GetFxRatesBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetFxRatesBatchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/fx");

        group.MapPost("/latest-batch", HandleAsync).RequireAuthorization(SystemCaller.PolicyName);

        return app;
    }

    private static async Task<Ok<FxRatesBatchResponse>> HandleAsync(
        FxRatesBatchRequest request, GetFxRatesBatchHandler handler, CancellationToken cancellationToken)
        => TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
